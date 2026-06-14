using System.Buffers;
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Aethra.Shared.Contracts.Containers;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Aethra.Satellite.Containers.Docker;

/// <summary>
/// Implementación de <see cref="IContainerRuntime"/> sobre <c>Docker.DotNet</c>.
/// Habla contra el socket Docker local: Unix socket en Linux, named pipe en Windows.
/// </summary>
public sealed class DockerContainerRuntime : IContainerRuntime, IDisposable
{
    // Usamos el tipo concreto DockerClient (no IDockerClient de Docker.DotNet) para evitar el
    // boxing/dispatch virtual cuando solo tenemos una implementación (CA1859).
    private readonly DockerClient _client;
    private readonly ILogger<DockerContainerRuntime> _logger;

    public DockerContainerRuntime(ILogger<DockerContainerRuntime> logger)
    {
        _logger = logger;
        var endpoint = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");
        _client = new DockerClientConfiguration(endpoint).CreateClient();
    }

    /// <summary>
    /// Construye una imagen. El central envía un tarball gzip-encoded como bytes; lo pasamos
    /// como stream al cliente Docker. Los progress messages se acumulan en una lista para
    /// retornarlos junto al resultado (útil para diagnóstico de builds fallidos).
    /// <para>
    /// F11.2: si <c>spec.Mode == BuildMode.Nixpacks</c>, delegamos a <see cref="BuildImageNixpacksAsync"/>
    /// que extrae el tarball y ejecuta <c>nixpacks build</c> contra el daemon Docker local.
    /// </para>
    /// </summary>
    public async Task<BuildResult> BuildImageAsync(BuildSpec spec, CancellationToken ct)
    {
        if (spec.Mode == BuildMode.Nixpacks)
        {
            return await BuildImageNixpacksAsync(spec, ct).ConfigureAwait(false);
        }

        // Dockerfile mode: construimos con el CLI `docker build` (no Docker.DotNet). El builder
        // legacy de Docker.DotNet NO soporta BuildKit, y los Dockerfiles modernos (p.ej. Next.js)
        // usan `RUN --mount=type=cache`, que requiere BuildKit. El CLI sí lo usa (forzamos
        // DOCKER_BUILDKIT=1). Extraemos el contexto tar.gz a un tempdir y delegamos al CLI.
        var logs = new List<string>();
        var tempDir = Path.Combine(Path.GetTempPath(), "aethra-docker-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            logs.Add($"Extrayendo contexto de build a {tempDir}...");
            await ExtractTarGzAsync(spec.BuildContextTarGz, tempDir, ct).ConfigureAwait(false);

            var dockerfile = string.IsNullOrWhiteSpace(spec.DockerfilePath) ? "Dockerfile" : spec.DockerfilePath!;
            var args = new List<string> { "build", "-t", spec.ImageRef, "-f", Path.Combine(tempDir, dockerfile) };
            foreach (var (k, v) in spec.BuildArgs)
            {
                args.Add("--build-arg");
                args.Add(string.Create(CultureInfo.InvariantCulture, $"{k}={v}"));
            }
            // Contexto de build: subdir del servicio si lo especifica (Dockerfiles que asumen
            // context=su subcarpeta, p.ej. un frontend), si no la raíz del tarball extraído.
            var contextDir = string.IsNullOrWhiteSpace(spec.BuildContextDir)
                ? tempDir
                : Path.Combine(tempDir, spec.BuildContextDir!);
            args.Add(contextDir); // build context

            logs.Add($"Ejecutando: docker {string.Join(' ', args)} (BuildKit)");
            var env = new Dictionary<string, string>(StringComparer.Ordinal) { ["DOCKER_BUILDKIT"] = "1" };
            var (exitCode, stdout, stderr) = await RunProcessAsync("docker", args, ct, env).ConfigureAwait(false);
            logs.AddRange(SplitLines(stdout));
            logs.AddRange(SplitLines(stderr));

            if (exitCode != 0)
            {
                return new BuildResult(Success: false, ImageId: null,
                    ErrorMessage: $"docker build salió con código {exitCode.ToString(CultureInfo.InvariantCulture)}", logs);
            }

            string? imageId = null;
            try
            {
                var inspect = await _client.Images.InspectImageAsync(spec.ImageRef, ct).ConfigureAwait(false);
                imageId = inspect.ID;
            }
            catch (DockerImageNotFoundException)
            {
                return new BuildResult(Success: false, ImageId: null,
                    ErrorMessage: $"docker build terminó OK pero la imagen {spec.ImageRef} no aparece en el daemon.", logs);
            }

            return new BuildResult(Success: true, imageId, ErrorMessage: null, logs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build (docker CLI) de imagen {ImageRef} falló", spec.ImageRef);
            return new BuildResult(Success: false, ImageId: null, ErrorMessage: ex.Message, logs);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// F11.2 — Build via Nixpacks. Requiere que el CLI <c>nixpacks</c> esté en el PATH del
    /// satélite (instalable con <c>curl -fsSL https://nixpacks.com/install.sh | bash</c>).
    /// El binario detecta el lenguaje (Node, Python, Go, Rust, Ruby, PHP, ...) del contexto
    /// y delega el build al daemon Docker local. La imagen queda etiquetada con <c>spec.ImageRef</c>.
    /// </summary>
    private async Task<BuildResult> BuildImageNixpacksAsync(BuildSpec spec, CancellationToken ct)
    {
        var logs = new List<string>();
        var tempDir = Path.Combine(Path.GetTempPath(), "aethra-nixpacks-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // 1. Pre-check: nixpacks debe existir en el PATH.
            if (!IsNixpacksAvailable())
            {
                const string msg = "El CLI 'nixpacks' no está instalado o no está en el PATH del satélite. "
                    + "Instalalo con: curl -fsSL https://nixpacks.com/install.sh | bash";
                logs.Add(msg);
                return new BuildResult(Success: false, ImageId: null,
                    ErrorMessage: "nixpacks_not_installed", logs);
            }

            // 2. Extraer el tarball gzip al tempdir.
            logs.Add($"Extrayendo contexto de build a {tempDir}...");
            await ExtractTarGzAsync(spec.BuildContextTarGz, tempDir, ct).ConfigureAwait(false);

            // 3. Construir args: nixpacks build <dir> --name <imageRef> [--build-arg ...] [--config ...]
            var args = new List<string> { "build", tempDir, "--name", spec.ImageRef };
            foreach (var (k, v) in spec.BuildArgs)
            {
                args.Add("--build-arg");
                args.Add(string.Create(CultureInfo.InvariantCulture, $"{k}={v}"));
            }
            if (!string.IsNullOrWhiteSpace(spec.NixpacksConfig))
            {
                args.Add("--config");
                args.Add(spec.NixpacksConfig);
            }

            logs.Add($"Ejecutando: nixpacks {string.Join(' ', args)}");

            // 4. Ejecutar nixpacks capturando stdout/stderr en logs.
            var (exitCode, stdout, stderr) = await RunProcessAsync("nixpacks", args, ct).ConfigureAwait(false);
            logs.AddRange(SplitLines(stdout));
            logs.AddRange(SplitLines(stderr));

            if (exitCode != 0)
            {
                return new BuildResult(Success: false, ImageId: null,
                    ErrorMessage: $"nixpacks build salió con código {exitCode.ToString(CultureInfo.InvariantCulture)}",
                    logs);
            }

            // 5. Verificar que la imagen quedó en el daemon Docker local.
            string? imageId = null;
            try
            {
                var inspect = await _client.Images.InspectImageAsync(spec.ImageRef, ct).ConfigureAwait(false);
                imageId = inspect.ID;
                logs.Add($"Imagen {spec.ImageRef} disponible en el daemon Docker local (id={imageId}).");
            }
            catch (DockerImageNotFoundException)
            {
                return new BuildResult(Success: false, ImageId: null,
                    ErrorMessage: $"nixpacks build terminó OK pero la imagen {spec.ImageRef} no aparece en el daemon Docker. "
                        + "Posible mismatch de socket o registry.",
                    logs);
            }

            return new BuildResult(Success: true, imageId, ErrorMessage: null, logs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build nixpacks de imagen {ImageRef} falló", spec.ImageRef);
            return new BuildResult(Success: false, ImageId: null, ex.Message, logs);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    public async Task<PushResult> PushImageAsync(string imageRef, RegistryAuth auth, CancellationToken ct)
    {
        try
        {
            string? digest = null;
            string? errorDetail = null;
            var progress = new Progress<JSONMessage>(msg =>
            {
                if (msg.ErrorMessage is { Length: > 0 } err)
                {
                    errorDetail = err;
                }
                // El registry suele devolver el digest en msg.Status del tipo "digest: sha256:..."
                if (msg.Status is { Length: > 0 } status)
                {
                    const string digestKey = "digest: sha256:";
                    var idx = status.IndexOf(digestKey, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        var start = idx + "digest: ".Length;
                        var end = status.IndexOf(' ', start);
                        digest = end > start ? status[start..end] : status[start..];
                    }
                }
            });

            var (name, tag) = SplitImageRef(imageRef);
            await _client.Images.PushImageAsync(
                name,
                new ImagePushParameters { Tag = tag },
                new AuthConfig
                {
                    ServerAddress = auth.Server,
                    Username = auth.Username,
                    Password = auth.Password,
                },
                progress,
                ct);

            if (errorDetail is not null)
            {
                return new PushResult(Success: false, Digest: null, errorDetail);
            }
            return new PushResult(Success: true, digest, ErrorMessage: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push de imagen {ImageRef} falló", imageRef);
            return new PushResult(Success: false, Digest: null, ex.Message);
        }
    }

    public async Task<PullResult> PullImageAsync(string imageRef, RegistryAuth? auth, CancellationToken ct)
    {
        try
        {
            var (fromImage, tag) = SplitImageRef(imageRef);
            AuthConfig? authConfig = auth is null
                ? null
                : new AuthConfig
                {
                    ServerAddress = auth.Server,
                    Username = auth.Username,
                    Password = auth.Password,
                };

            string? errorDetail = null;
            var progress = new Progress<JSONMessage>(msg =>
            {
                if (msg.ErrorMessage is { Length: > 0 } err)
                {
                    errorDetail = err;
                }
            });

            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = fromImage, Tag = tag },
                authConfig,
                progress,
                ct);

            if (errorDetail is not null)
            {
                return new PullResult(Success: false, ImageId: null, errorDetail);
            }

            // Tras el pull, inspeccionamos para devolver el image ID local.
            try
            {
                var inspect = await _client.Images.InspectImageAsync(imageRef, ct);
                return new PullResult(Success: true, inspect.ID, ErrorMessage: null);
            }
            catch (DockerImageNotFoundException)
            {
                return new PullResult(Success: true, ImageId: null, ErrorMessage: null);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pull de imagen {ImageRef} falló", imageRef);
            return new PullResult(Success: false, ImageId: null, ex.Message);
        }
    }

    /// <summary>
    /// Crea + arranca un contenedor. Si la imagen no está local, hace pull anónimo primero
    /// (para pulls autenticados use <see cref="PullImageAsync"/> explícitamente antes).
    /// </summary>
    public async Task<RunResult> RunContainerAsync(RunSpec spec, CancellationToken ct)
    {
        try
        {
            await EnsureImageAvailableAsync(spec.ImageRef, ct);

            var envList = spec.Env.Select(kv => string.Create(
                CultureInfo.InvariantCulture, $"{kv.Key}={kv.Value}")).ToList();

            // Mapeo de puertos: ExposedPorts declara el puerto del contenedor; HostConfig.PortBindings
            // ata cada uno al host. Si HostPort es null, lo dejamos vacío y Docker asigna ephemeral.
            // HostIP por defecto = 127.0.0.1 (loopback): el proxy alcanza los contenedores por DNS de
            // Docker en la red interna, así que el puerto en el host es solo para health-check/diagnóstico
            // y NO debe quedar público (0.0.0.0). Para exponer público pasar HostIp="0.0.0.0" explícito.
            var exposedPorts = new Dictionary<string, EmptyStruct>(StringComparer.Ordinal);
            var portBindings = new Dictionary<string, IList<global::Docker.DotNet.Models.PortBinding>>(StringComparer.Ordinal);
            foreach (var p in spec.Ports)
            {
                var key = string.Create(CultureInfo.InvariantCulture, $"{p.ContainerPort}/{p.Protocol.ToLowerInvariant()}");
                exposedPorts[key] = default;
                portBindings[key] = new List<global::Docker.DotNet.Models.PortBinding>
                {
                    new()
                    {
                        HostIP = string.IsNullOrWhiteSpace(p.HostIp) ? "127.0.0.1" : p.HostIp,
                        HostPort = p.HostPort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    },
                };
            }

            var binds = spec.Volumes
                .Select(v => string.Create(CultureInfo.InvariantCulture,
                    $"{v.NameOrHostPath}:{v.ContainerPath}{(v.ReadOnly ? ":ro" : "")}"))
                .ToList();

            var createParams = new CreateContainerParameters
            {
                Name = spec.ContainerName,
                Image = spec.ImageRef,
                Env = envList,
                ExposedPorts = exposedPorts,
                HostConfig = new HostConfig
                {
                    PortBindings = portBindings,
                    Binds = binds,
                    NetworkMode = spec.NetworkName,
                    RestartPolicy = ParseRestartPolicy(spec.RestartPolicy),
                },
            };

            if (spec.Command is { Count: > 0 } cmd)
            {
                createParams.Cmd = [.. cmd];
            }

            if (spec.Healthcheck is { } hc)
            {
                createParams.Healthcheck = new HealthConfig
                {
                    Test = [.. hc.Test],
                    Interval = TimeSpan.FromSeconds(hc.IntervalSeconds),
                    Timeout = hc.TimeoutSeconds is int t ? TimeSpan.FromSeconds(t) : TimeSpan.Zero,
                    StartPeriod = hc.StartPeriodSeconds is int sp ? sp * 1_000_000_000L : 0L,
                    Retries = hc.Retries,
                };
            }

            var created = await _client.Containers.CreateContainerAsync(createParams, ct);
            var started = await _client.Containers.StartContainerAsync(
                created.ID, new ContainerStartParameters(), ct);
            if (!started)
            {
                return new RunResult(
                    Success: false, ContainerId: created.ID,
                    ErrorMessage: "Docker reportó que el contenedor no arrancó");
            }
            return new RunResult(Success: true, created.ID, ErrorMessage: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run de contenedor {Name} falló", spec.ContainerName);
            return new RunResult(Success: false, ContainerId: null, ErrorMessage: ex.Message);
        }
    }

    public async Task StopContainerAsync(string nameOrId, CancellationToken ct)
    {
        await _client.Containers.StopContainerAsync(
            nameOrId,
            new ContainerStopParameters { WaitBeforeKillSeconds = 30 },
            ct);
    }

    public async Task RestartContainerAsync(string nameOrId, CancellationToken ct)
    {
        await _client.Containers.RestartContainerAsync(
            nameOrId,
            new ContainerRestartParameters { WaitBeforeKillSeconds = 30 },
            ct);
    }

    public async Task RemoveContainerAsync(string nameOrId, bool force, CancellationToken ct)
    {
        await _client.Containers.RemoveContainerAsync(
            nameOrId,
            new ContainerRemoveParameters { Force = force, RemoveVolumes = false },
            ct);
    }

    public async Task<IReadOnlyList<string>> PruneImageRepoAsync(string repository, int keepLast, CancellationToken ct)
    {
        if (keepLast <= 0 || string.IsNullOrWhiteSpace(repository))
        {
            return [];
        }

        var prefix = repository + ":";
        var all = await _client.Images
            .ListImagesAsync(new ImagesListParameters { All = false }, ct)
            .ConfigureAwait(false);

        // Imágenes con al menos un tag de este repositorio, más recientes primero.
        var ofRepo = all
            .Where(i => i.RepoTags is not null
                && i.RepoTags.Any(t => t.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderByDescending(i => i.Created)
            .ToList();

        var removed = new List<string>();
        foreach (var image in ofRepo.Skip(keepLast))
        {
            foreach (var tag in image.RepoTags.Where(t => t.StartsWith(prefix, StringComparison.Ordinal)))
            {
                try
                {
                    // Force=false: si la imagen está en uso por un contenedor, Docker la rechaza
                    // y la dejamos intacta (red de seguridad — nunca rompemos algo corriendo).
                    await _client.Images
                        .DeleteImageAsync(tag, new ImageDeleteParameters { Force = false }, ct)
                        .ConfigureAwait(false);
                    removed.Add(tag);
                }
                catch (DockerApiException ex)
                {
                    _logger.LogDebug(ex, "Retención: no se pudo borrar {Tag} (probablemente en uso); se omite.", tag);
                }
            }
        }
        return removed;
    }

    public async Task<string?> PruneBuildCacheAsync(int maxAgeHours, CancellationToken ct)
    {
        if (maxAgeHours <= 0)
        {
            return null;
        }

        // `docker builder prune -f --filter until=<h>h`: borra cache de build no referenciado en las
        // últimas N horas. No toca el cache reciente (rebuilds siguen rápidos) ni imágenes/contenedores.
        var until = "until=" + maxAgeHours.ToString(CultureInfo.InvariantCulture) + "h";
        var (code, stdout, _) = await RunProcessAsync(
            "docker", ["builder", "prune", "-f", "--filter", until], ct).ConfigureAwait(false);
        if (code != 0)
        {
            return null;
        }

        // La CLI imprime "Total reclaimed space: <N>" al final; devolvemos esa línea como resumen.
        return SplitLines(stdout).LastOrDefault(l => l.Contains("reclaimed", StringComparison.OrdinalIgnoreCase))
            ?? "build cache pruned";
    }

    /// <summary>
    /// Streamea logs en formato multiplexado. Docker antepone un header de 8 bytes por frame:
    /// <c>[StreamType, 0,0,0, BigEndianSizeUInt32]</c>. Docker.DotNet expone
    /// <c>MultiplexedStream.ReadOutputAsync</c> que devuelve un <c>ReadResult</c>
    /// con Target ya resuelto (StandardOut/StandardError), evitando que tengamos que parsear el header.
    /// </summary>
    public async IAsyncEnumerable<string> StreamLogsAsync(
        string nameOrId, int tailLines, [EnumeratorCancellation] CancellationToken ct)
    {
        var parameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Follow = true,
            Tail = tailLines > 0 ? tailLines.ToString(CultureInfo.InvariantCulture) : "all",
        };

        using var stream = await _client.Containers.GetContainerLogsAsync(
            nameOrId, tty: false, parameters, ct);

        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        var pending = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
                if (result.EOF)
                {
                    if (pending.Length > 0)
                    {
                        yield return pending.ToString();
                        pending.Clear();
                    }
                    yield break;
                }
                if (result.Count <= 0)
                {
                    continue;
                }
                pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                // Emitimos por línea para que el central pueda multiplexar contra varios listeners.
                while (true)
                {
                    var text = pending.ToString();
                    var newlineIdx = text.IndexOf('\n', StringComparison.Ordinal);
                    if (newlineIdx < 0)
                    {
                        break;
                    }
                    var line = text[..newlineIdx].TrimEnd('\r');
                    pending.Clear();
                    pending.Append(text[(newlineIdx + 1)..]);
                    yield return line;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// F12.1A — ejecuta un comando shell dentro de un contenedor corriendo. Usa la API
    /// nativa de Docker (<c>docker exec</c>) para crear un proceso exec, attach a stdout/stderr,
    /// y leer hasta que el proceso termine o se exceda <paramref name="timeoutSeconds"/>.
    /// </summary>
    public async Task<ExecResult> ExecInContainerAsync(
        string containerNameOrId, string command, int timeoutSeconds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerNameOrId))
        {
            return new ExecResult(-1, string.Empty, "container_name_required", TimedOut: false);
        }
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ExecResult(-1, string.Empty, "command_required", TimedOut: false);
        }
        var timeoutSec = timeoutSeconds <= 0 ? 300 : timeoutSeconds;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            var execParams = new ContainerExecCreateParameters
            {
                AttachStdout = true,
                AttachStderr = true,
                Tty = false,
                Cmd = new List<string> { "sh", "-c", command },
            };
            var execCreate = await _client.Exec.ExecCreateContainerAsync(
                containerNameOrId, execParams, linked.Token).ConfigureAwait(false);

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            using (var stream = await _client.Exec.StartAndAttachContainerExecAsync(
                execCreate.ID, tty: false, linked.Token).ConfigureAwait(false))
            {
                var (stdout, stderr) = await stream.ReadOutputToEndAsync(linked.Token).ConfigureAwait(false);
                stdoutBuilder.Append(stdout);
                stderrBuilder.Append(stderr);
            }

            var inspect = await _client.Exec.InspectContainerExecAsync(execCreate.ID, linked.Token)
                .ConfigureAwait(false);
            var exitCode = (int)inspect.ExitCode;
            return new ExecResult(exitCode, stdoutBuilder.ToString(), stderrBuilder.ToString(), TimedOut: false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new ExecResult(-1, string.Empty, $"exec timed out after {timeoutSec}s", TimedOut: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Exec en contenedor {Name} falló", containerNameOrId);
            return new ExecResult(-1, string.Empty, ex.Message, TimedOut: false);
        }
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken ct)
    {
        var raw = await _client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true }, ct);
        return raw.Select(c => new ContainerInfo(
            Id: c.ID,
            // Docker prefija los nombres con "/". Lo quitamos para devolverlo limpio.
            Name: c.Names is { Count: > 0 } ? c.Names[0].TrimStart('/') : string.Empty,
            Image: c.Image ?? string.Empty,
            Status: c.Status ?? string.Empty,
            ExposedPorts: c.Ports is { Count: > 0 }
                ? [.. c.Ports.Select(p => (int)p.PrivatePort)]
                : []))
            .ToList();
    }

    /// <summary>
    /// Pull anónimo de la imagen si no está local. Docker.DotNet no tiene una API "exists"
    /// directa, pero <c>InspectImageAsync</c> lanza <c>DockerImageNotFoundException</c> si falta.
    /// </summary>
    private async Task EnsureImageAvailableAsync(string imageRef, CancellationToken ct)
    {
        try
        {
            await _client.Images.InspectImageAsync(imageRef, ct);
            return; // ya está local
        }
        catch (DockerImageNotFoundException)
        {
            // Caemos al pull anónimo.
        }

        var (fromImage, tag) = SplitImageRef(imageRef);
        await _client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = fromImage, Tag = tag },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            ct);
    }

    private static RestartPolicy? ParseRestartPolicy(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
        {
            return null;
        }
        return policy.ToLowerInvariant() switch
        {
            "no" => new RestartPolicy { Name = RestartPolicyKind.No },
            "always" => new RestartPolicy { Name = RestartPolicyKind.Always },
            "on-failure" => new RestartPolicy { Name = RestartPolicyKind.OnFailure },
            "unless-stopped" => new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
            _ => null,
        };
    }

    private static (string Name, string Tag) SplitImageRef(string imageRef)
    {
        // Si tiene digest (@sha256:...) lo pasamos completo como Name sin tag.
        var atIdx = imageRef.IndexOf('@', StringComparison.Ordinal);
        if (atIdx > 0)
        {
            return (imageRef, string.Empty);
        }
        // Separamos por el ÚLTIMO ':' después del último '/' para no confundir con puerto en el registry.
        var slashIdx = imageRef.LastIndexOf('/');
        var colonIdx = imageRef.LastIndexOf(':');
        if (colonIdx > slashIdx && colonIdx > 0)
        {
            return (imageRef[..colonIdx], imageRef[(colonIdx + 1)..]);
        }
        return (imageRef, "latest");
    }

    // -------------------------------------------------------------------------
    // F11.2 helpers compartidos con la rama Nixpacks.
    // -------------------------------------------------------------------------

    private static bool IsNixpacksAvailable()
    {
        // Estrategia simple: lanzar "nixpacks --version" y considerarlo disponible si arranca
        // sin Win32Exception. Evita un cache para no romper cuando el operador instala nixpacks
        // tras arrancar el satélite.
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("nixpacks", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            p.Start();
            p.WaitForExit(5_000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName, IReadOnlyList<string> args, CancellationToken ct,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (env is not null)
        {
            foreach (var (k, v) in env)
            {
                psi.Environment[k] = v;
            }
        }
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) { stdoutBuilder.AppendLine(e.Data); }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) { stderrBuilder.AppendLine(e.Data); }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
            throw;
        }

        return (proc.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    private static async Task ExtractTarGzAsync(byte[] tarGz, string destDir, CancellationToken ct)
    {
        await using var ms = new MemoryStream(tarGz, writable: false);
        await using var gz = new GZipStream(ms, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gz, destDir, overwriteFiles: true, ct).ConfigureAwait(false);
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }
        using var sr = new StringReader(text);
        while (sr.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }

    public void Dispose() => _client.Dispose();
}
