using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aethra.Shared.Contracts.Containers;
using VmContainerInfo = Aethra.Shared.Contracts.Vms.ContainerInfo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Satellite.Containers.Podman;

/// <summary>
/// Implementación de <see cref="IContainerRuntime"/> sobre el CLI <c>podman</c>.
/// <para>
/// Podman no expone un socket REST estable equivalente a Docker.DotNet en todas las
/// distros, así que delegamos en el binario CLI vía <see cref="Process"/>. Cada método
/// arma el comando, lo ejecuta, captura stdout/stderr y parsea el resultado.
/// </para>
/// </summary>
public sealed partial class PodmanContainerRuntime : IContainerRuntime
{
    // Cache estática de las opciones del serializador JSON. CA1869 pide no construirlo en cada llamada.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"sha256:[0-9a-f]{64}", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    private readonly string _podmanBin;
    private readonly ILogger<PodmanContainerRuntime> _logger;

    public PodmanContainerRuntime(IOptions<PodmanOptions> opts, ILogger<PodmanContainerRuntime> logger)
    {
        _podmanBin = string.IsNullOrWhiteSpace(opts.Value.BinaryPath) ? "podman" : opts.Value.BinaryPath!;
        _logger = logger;
    }

    public async Task<BuildResult> BuildImageAsync(BuildSpec spec, CancellationToken ct)
    {
        if (spec.Mode == BuildMode.Nixpacks)
        {
            return await BuildImageNixpacksAsync(spec, ct).ConfigureAwait(false);
        }

        // 1. Materializar el tarball en un tempdir y extraerlo.
        var tempDir = Path.Combine(Path.GetTempPath(), "aethra-podman-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var logs = new List<string>();
        try
        {
            await ExtractTarGzAsync(spec.BuildContextTarGz, tempDir, ct);

            // 2. Construir argumentos: podman build -t {imageRef} -f {dockerfile} [--build-arg K=V ...] {contextDir}
            var args = new List<string> { "build", "-t", spec.ImageRef, "-f", spec.DockerfilePath };
            foreach (var (k, v) in spec.BuildArgs)
            {
                args.Add("--build-arg");
                args.Add(string.Create(CultureInfo.InvariantCulture, $"{k}={v}"));
            }
            args.Add(tempDir);

            var (exitCode, stdout, stderr) = await RunPodmanAsync(args, ct);
            logs.AddRange(SplitLines(stdout));
            logs.AddRange(SplitLines(stderr));

            if (exitCode != 0)
            {
                return new BuildResult(Success: false, ImageId: null,
                    ErrorMessage: $"podman build salió con código {exitCode.ToString(CultureInfo.InvariantCulture)}", logs);
            }

            // 3. Parsear el último sha256 de stdout/stderr como image ID.
            var match = Sha256Regex().Matches(stdout + "\n" + stderr).LastOrDefault();
            var imageId = match?.Value;
            return new BuildResult(Success: true, imageId, ErrorMessage: null, logs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build podman de imagen {ImageRef} falló", spec.ImageRef);
            return new BuildResult(Success: false, ImageId: null, ex.Message, logs);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// F11.2 — Build via Nixpacks contra el daemon Podman local. Requiere que <c>nixpacks</c>
    /// esté en el PATH; el binario delega el build al socket Podman si <c>DOCKER_HOST</c> apunta
    /// allí (típicamente <c>unix:///run/user/$UID/podman/podman.sock</c> en rootless).
    /// </summary>
    private async Task<BuildResult> BuildImageNixpacksAsync(BuildSpec spec, CancellationToken ct)
    {
        var logs = new List<string>();
        var tempDir = Path.Combine(Path.GetTempPath(), "aethra-nixpacks-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            if (!IsNixpacksAvailable())
            {
                const string msg = "El CLI 'nixpacks' no está instalado o no está en el PATH del satélite. "
                    + "Instalalo con: curl -fsSL https://nixpacks.com/install.sh | bash";
                logs.Add(msg);
                return new BuildResult(Success: false, ImageId: null,
                    ErrorMessage: "nixpacks_not_installed", logs);
            }

            logs.Add($"Extrayendo contexto de build a {tempDir}...");
            await ExtractTarGzAsync(spec.BuildContextTarGz, tempDir, ct).ConfigureAwait(false);

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

            var (exitCode, stdout, stderr) = await RunArbitraryAsync("nixpacks", args, ct).ConfigureAwait(false);
            logs.AddRange(SplitLines(stdout));
            logs.AddRange(SplitLines(stderr));

            if (exitCode != 0)
            {
                return new BuildResult(Success: false, ImageId: null,
                    ErrorMessage: $"nixpacks build salió con código {exitCode.ToString(CultureInfo.InvariantCulture)}",
                    logs);
            }

            // Verificar que la imagen existe localmente: podman images <ref> --format '{{.ID}}'
            var (inspectExit, inspectOut, inspectErr) = await RunPodmanAsync(
                ["images", spec.ImageRef, "--format", "{{.ID}}"], ct).ConfigureAwait(false);
            if (inspectExit != 0 || string.IsNullOrWhiteSpace(inspectOut))
            {
                return new BuildResult(Success: false, ImageId: null,
                    ErrorMessage: $"nixpacks build terminó OK pero la imagen {spec.ImageRef} no aparece en podman. "
                        + $"stderr={inspectErr.Trim()}",
                    logs);
            }

            var imageId = inspectOut.Trim().Split('\n').FirstOrDefault()?.Trim();
            logs.Add($"Imagen {spec.ImageRef} disponible en podman (id={imageId}).");
            return new BuildResult(Success: true, imageId, ErrorMessage: null, logs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build nixpacks (podman) de imagen {ImageRef} falló", spec.ImageRef);
            return new BuildResult(Success: false, ImageId: null, ex.Message, logs);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static bool IsNixpacksAvailable()
    {
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

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunArbitraryAsync(
        string fileName, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
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
        var stderrText = stderrBuilder.ToString();
        if (!string.IsNullOrWhiteSpace(stderrText))
        {
            _logger.LogDebug("{Bin} stderr: {Stderr}", fileName, stderrText);
        }
        return (proc.ExitCode, stdoutBuilder.ToString(), stderrText);
    }

    public async Task<PushResult> PushImageAsync(string imageRef, RegistryAuth auth, CancellationToken ct)
    {
        var args = new List<string>
        {
            "push",
            "--creds", string.Create(CultureInfo.InvariantCulture, $"{auth.Username}:{auth.Password}"),
            imageRef,
        };
        var (exitCode, stdout, stderr) = await RunPodmanAsync(args, ct);
        if (exitCode != 0)
        {
            return new PushResult(Success: false, Digest: null,
                ErrorMessage: $"podman push salió con código {exitCode.ToString(CultureInfo.InvariantCulture)}: {stderr}");
        }
        var digest = Sha256Regex().Matches(stdout + "\n" + stderr).LastOrDefault()?.Value;
        return new PushResult(Success: true, digest, ErrorMessage: null);
    }

    public async Task<PullResult> PullImageAsync(string imageRef, RegistryAuth? auth, CancellationToken ct)
    {
        var args = new List<string> { "pull" };
        if (auth is not null)
        {
            args.Add("--creds");
            args.Add(string.Create(CultureInfo.InvariantCulture, $"{auth.Username}:{auth.Password}"));
        }
        args.Add(imageRef);
        var (exitCode, stdout, stderr) = await RunPodmanAsync(args, ct);
        if (exitCode != 0)
        {
            return new PullResult(Success: false, ImageId: null,
                ErrorMessage: $"podman pull salió con código {exitCode.ToString(CultureInfo.InvariantCulture)}: {stderr}");
        }
        var imageId = Sha256Regex().Matches(stdout).LastOrDefault()?.Value;
        return new PullResult(Success: true, imageId, ErrorMessage: null);
    }

    public async Task<RunResult> RunContainerAsync(RunSpec spec, CancellationToken ct)
    {
        try
        {
            var args = new List<string> { "run", "-d", "--name", spec.ContainerName };

            foreach (var (k, v) in spec.Env)
            {
                args.Add("-e");
                args.Add(string.Create(CultureInfo.InvariantCulture, $"{k}={v}"));
            }

            foreach (var p in spec.Ports)
            {
                args.Add("-p");
                // HostIp por defecto = 127.0.0.1 (loopback): los contenedores nativos se alcanzan por
                // DNS interno vía el proxy; el puerto en el host es solo health-check/diagnóstico y no
                // debe quedar público. Para exponer público pasar HostIp="0.0.0.0". El doble ":" deja
                // que el runtime asigne un puerto ephemeral en esa interfaz.
                var ip = string.IsNullOrWhiteSpace(p.HostIp) ? "127.0.0.1" : p.HostIp;
                var proto = p.Protocol.ToLowerInvariant();
                args.Add(p.HostPort is int hp
                    ? string.Create(CultureInfo.InvariantCulture, $"{ip}:{hp}:{p.ContainerPort}/{proto}")
                    : string.Create(CultureInfo.InvariantCulture, $"{ip}::{p.ContainerPort}/{proto}"));
            }

            foreach (var v in spec.Volumes)
            {
                args.Add("-v");
                args.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{v.NameOrHostPath}:{v.ContainerPath}{(v.ReadOnly ? ":ro" : "")}"));
            }

            if (!string.IsNullOrWhiteSpace(spec.NetworkName))
            {
                args.Add("--network");
                args.Add(spec.NetworkName);
            }

            if (!string.IsNullOrWhiteSpace(spec.RestartPolicy))
            {
                args.Add("--restart");
                args.Add(spec.RestartPolicy);
            }

            if (spec.Healthcheck is { } hc)
            {
                args.Add("--health-cmd");
                args.Add(string.Join(" ", hc.Test.Skip(hc.Test.Count > 0
                    && (hc.Test[0] == "CMD" || hc.Test[0] == "CMD-SHELL") ? 1 : 0)));
                args.Add("--health-interval");
                args.Add(string.Create(CultureInfo.InvariantCulture, $"{hc.IntervalSeconds}s"));
                args.Add("--health-retries");
                args.Add(hc.Retries.ToString(CultureInfo.InvariantCulture));
                if (hc.TimeoutSeconds is int t)
                {
                    args.Add("--health-timeout");
                    args.Add(string.Create(CultureInfo.InvariantCulture, $"{t}s"));
                }
                if (hc.StartPeriodSeconds is int sp)
                {
                    args.Add("--health-start-period");
                    args.Add(string.Create(CultureInfo.InvariantCulture, $"{sp}s"));
                }
            }

            args.Add(spec.ImageRef);

            if (spec.Command is { Count: > 0 } cmd)
            {
                args.AddRange(cmd);
            }

            var (exitCode, stdout, stderr) = await RunPodmanAsync(args, ct);
            if (exitCode != 0)
            {
                return new RunResult(Success: false, ContainerId: null,
                    ErrorMessage: $"podman run salió con código {exitCode.ToString(CultureInfo.InvariantCulture)}: {stderr}");
            }
            // podman run -d imprime el ID del contenedor en stdout.
            var containerId = stdout.Trim();
            return new RunResult(Success: true, containerId, ErrorMessage: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run podman de contenedor {Name} falló", spec.ContainerName);
            return new RunResult(Success: false, ContainerId: null, ex.Message);
        }
    }

    public async Task StopContainerAsync(string nameOrId, CancellationToken ct)
    {
        var (exitCode, _, stderr) = await RunPodmanAsync(["stop", nameOrId], ct);
        if (exitCode != 0)
        {
            _logger.LogWarning("podman stop {Name} salió con código {Code}: {Stderr}",
                nameOrId, exitCode, stderr);
        }
    }

    public async Task RestartContainerAsync(string nameOrId, CancellationToken ct)
    {
        var (exitCode, _, stderr) = await RunPodmanAsync(["restart", nameOrId], ct);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"podman restart {nameOrId} salio con codigo {exitCode.ToString(CultureInfo.InvariantCulture)}: {stderr}");
        }
    }

    public async Task RemoveContainerAsync(string nameOrId, bool force, CancellationToken ct)
    {
        var args = new List<string> { "rm" };
        if (force)
        {
            args.Add("-f");
        }
        args.Add(nameOrId);
        var (exitCode, _, stderr) = await RunPodmanAsync(args, ct);
        if (exitCode != 0)
        {
            _logger.LogWarning("podman rm {Name} salió con código {Code}: {Stderr}",
                nameOrId, exitCode, stderr);
        }
    }

    public async Task<IReadOnlyList<string>> PruneImageRepoAsync(string repository, int keepLast, CancellationToken ct)
    {
        if (keepLast <= 0 || string.IsNullOrWhiteSpace(repository))
        {
            return [];
        }

        var (code, stdout, _) = await RunPodmanAsync(
            ["images", repository, "--format", "{{.CreatedAt}}|{{.Repository}}:{{.Tag}}"], ct).ConfigureAwait(false);
        if (code != 0)
        {
            return [];
        }

        var prefix = repository + ":";
        // CreatedAt viene como "2026-06-13 14:00:22 ..." (año primero) ⇒ orden lexicográfico = cronológico.
        var refs = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', 2))
            .Where(p => p.Length == 2
                && p[1].StartsWith(prefix, StringComparison.Ordinal)
                && !p[1].EndsWith(":<none>", StringComparison.Ordinal))
            .OrderByDescending(p => p[0], StringComparer.Ordinal)
            .Select(p => p[1])
            .ToList();

        var removed = new List<string>();
        foreach (var imageRef in refs.Skip(keepLast))
        {
            // rmi sin -f: si la imagen está en uso, podman la rechaza y la dejamos intacta.
            var (rc, _, stderr) = await RunPodmanAsync(["rmi", imageRef], ct).ConfigureAwait(false);
            if (rc == 0)
            {
                removed.Add(imageRef);
            }
            else
            {
                _logger.LogDebug("Retención podman: no se pudo borrar {Ref} (en uso?): {Err}", imageRef, stderr);
            }
        }
        return removed;
    }

    public async Task<string?> PruneBuildCacheAsync(int maxAgeHours, int keepStorageGb, CancellationToken ct)
    {
        if (keepStorageGb <= 0 && maxAgeHours <= 0)
        {
            return null;
        }

        // Podman 4+ soporta `podman builder prune`. Con keepStorageGb usamos `--keep-storage <N>GB`
        // (tope de tamaño); sin él, filtro por edad. Best-effort: si no existe o falla, no-op.
        string[] args = keepStorageGb > 0
            ? ["builder", "prune", "-f", "--keep-storage", keepStorageGb.ToString(CultureInfo.InvariantCulture) + "GB"]
            : ["builder", "prune", "-f", "--filter", "until=" + maxAgeHours.ToString(CultureInfo.InvariantCulture) + "h"];

        var (code, stdout, _) = await RunPodmanAsync(args, ct).ConfigureAwait(false);
        if (code != 0 && keepStorageGb > 0)
        {
            (code, stdout, _) = await RunPodmanAsync(["builder", "prune", "-f"], ct).ConfigureAwait(false);
        }
        if (code != 0)
        {
            return null;
        }

        return SplitLines(stdout).LastOrDefault(l => l.Contains("reclaimed", StringComparison.OrdinalIgnoreCase))
            ?? "build cache pruned";
    }

    public async Task<string?> PruneAllBuildCacheAsync(CancellationToken ct)
    {
        // Backstop DURO: `podman builder prune -af` reclama TODO el build cache, incl. cache mounts
        // (lo único que los acota de forma fiable; el tope por tamaño/edad no los toca). El próximo
        // build queda "frío"; aceptable para un backstop periódico que garantiza no llenar el disco.
        var (code, stdout, _) = await RunPodmanAsync(["builder", "prune", "-af"], ct).ConfigureAwait(false);
        if (code != 0)
        {
            return null;
        }
        return SplitLines(stdout).LastOrDefault(l => l.Contains("reclaimed", StringComparison.OrdinalIgnoreCase))
            ?? "all build cache pruned (--all)";
    }

    public async Task<string?> PruneDanglingImagesAsync(CancellationToken ct)
    {
        // `podman image prune -f`: sólo imágenes colgantes (sin tag). Nunca toca imágenes en uso.
        var (code, stdout, _) = await RunPodmanAsync(["image", "prune", "-f"], ct).ConfigureAwait(false);
        if (code != 0)
        {
            return null;
        }
        return SplitLines(stdout).LastOrDefault(l => l.Contains("reclaimed", StringComparison.OrdinalIgnoreCase))
            ?? "dangling images pruned";
    }

    public async Task<string?> PruneAnonymousVolumesAsync(CancellationToken ct)
    {
        // dangling=true lista volúmenes sin contenedor. Filtramos además por nombre anónimo (64 hex)
        // para NUNCA tocar named volumes de datos/DP-keys (que el daemon también marca dangling cuando
        // su contenedor está caído entre deploys). rm sin -f: un volumen en uso lo rechaza podman.
        var (code, stdout, _) = await RunPodmanAsync(
            ["volume", "ls", "--filter", "dangling=true", "--format", "{{.Name}}"], ct).ConfigureAwait(false);
        if (code != 0)
        {
            return null;
        }

        var anon = SplitLines(stdout)
            .Select(l => l.Trim())
            .Where(n => n.Length > 0 && AnonymousVolumeNameRegex().IsMatch(n))
            .ToList();

        var removed = 0;
        foreach (var name in anon)
        {
            var (rc, _, stderr) = await RunPodmanAsync(["volume", "rm", name], ct).ConfigureAwait(false);
            if (rc == 0)
            {
                removed++;
            }
            else
            {
                _logger.LogDebug("Prune podman: no se pudo borrar volumen {Name} (en uso?): {Err}", name, stderr);
            }
        }
        return removed == 0 ? null : $"volúmenes anónimos podados: {removed}";
    }

    // Nombre de volumen anónimo: 64 hex en minúscula. Excluye todos los named volumes.
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AnonymousVolumeNameRegex();

    public async IAsyncEnumerable<string> StreamLogsAsync(
        string nameOrId, int tailLines, [EnumeratorCancellation] CancellationToken ct)
    {
        var args = new List<string> { "logs", "--follow" };
        if (tailLines > 0)
        {
            args.Add("--tail");
            args.Add(tailLines.ToString(CultureInfo.InvariantCulture));
        }
        args.Add(nameOrId);

        var psi = BuildProcessStartInfo(args);
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();

        // Drenamos stdout línea a línea, capturando stderr en buffer aparte.
        var stderrTask = Task.Run(async () =>
        {
            try
            {
                var stderr = await proc.StandardError.ReadToEndAsync(ct);
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    _logger.LogWarning("podman logs {Name} stderr: {Stderr}", nameOrId, stderr);
                }
            }
            catch (OperationCanceledException) { }
        }, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await proc.StandardOutput.ReadLineAsync(ct);
                if (line is null)
                {
                    yield break;
                }
                yield return line;
            }
        }
        finally
        {
            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
            try { await stderrTask; } catch (OperationCanceledException) { }
        }
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken ct)
    {
        // podman ps -a --format=json devuelve un array JSON con todos los contenedores.
        var (exitCode, stdout, stderr) = await RunPodmanAsync(["ps", "-a", "--format=json"], ct);
        if (exitCode != 0)
        {
            _logger.LogWarning("podman ps salió con código {Code}: {Stderr}", exitCode, stderr);
            return [];
        }
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return [];
        }
        try
        {
            var rows = JsonSerializer.Deserialize<List<PodmanPsRow>>(stdout, JsonOptions) ?? [];
            return [.. rows.Select(r => new ContainerInfo(
                Id: r.Id ?? string.Empty,
                Name: r.Names is { Count: > 0 } ? r.Names[0] : string.Empty,
                Image: r.Image ?? string.Empty,
                Status: r.Status ?? r.State ?? string.Empty,
                ExposedPorts: r.Ports is { Count: > 0 }
                    ? [.. r.Ports.Where(p => p.ContainerPort > 0).Select(p => p.ContainerPort)]
                    : []))];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "podman ps devolvió JSON no parseable");
            return [];
        }
    }

    public async Task<IReadOnlyList<VmContainerInfo>> ListContainerStatsAsync(CancellationToken ct)
    {
        // Inventario completo (todos los contenedores). Podman es el runtime secundario (prod usa
        // Docker): devolvemos estado/puertos correctos pero dejamos las stats de uso en null —
        // parsear `podman stats` (strings tipo "1.2MB / 4GB", formato variable entre versiones) es
        // frágil; el contrato admite null y la UI degrada con guiones.
        var (exitCode, stdout, stderr) = await RunPodmanAsync(["ps", "-a", "--format=json"], ct).ConfigureAwait(false);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            if (exitCode != 0)
            {
                _logger.LogWarning("podman ps (stats) salió con código {Code}: {Stderr}", exitCode, stderr);
            }
            return [];
        }
        try
        {
            var rows = JsonSerializer.Deserialize<List<PodmanPsRow>>(stdout, JsonOptions) ?? [];
            return [.. rows.Select(r => new VmContainerInfo(
                Id: r.Id ?? string.Empty,
                Name: r.Names is { Count: > 0 } ? r.Names[0] : string.Empty,
                Image: r.Image ?? string.Empty,
                Status: r.Status ?? r.State ?? string.Empty,
                State: r.State ?? string.Empty,
                CreatedAt: default,
                Ports: r.Ports is { Count: > 0 }
                    ? [.. r.Ports.Where(p => p.ContainerPort > 0).Select(p => p.ContainerPort)]
                    : []))];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "podman ps (stats) devolvió JSON no parseable");
            return [];
        }
    }

    /// <summary>
    /// F12.1A — exec via <c>podman exec</c> CLI. Captura stdout/stderr y maneja timeout
    /// matando el proceso podman si excede <paramref name="timeoutSeconds"/>.
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

        var args = new List<string> { "exec", containerNameOrId, "sh", "-c", command };
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            var (exitCode, stdout, stderr) = await RunPodmanAsync(args, linked.Token).ConfigureAwait(false);
            return new ExecResult(exitCode, stdout, stderr, TimedOut: false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new ExecResult(-1, string.Empty, $"exec timed out after {timeoutSec}s", TimedOut: true);
        }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunPodmanAsync(
        IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = BuildProcessStartInfo(args);
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
            throw;
        }

        var stderrText = stderrBuilder.ToString();
        if (!string.IsNullOrWhiteSpace(stderrText))
        {
            _logger.LogDebug("podman stderr: {Stderr}", stderrText);
        }
        return (proc.ExitCode, stdoutBuilder.ToString(), stderrText);
    }

    private ProcessStartInfo BuildProcessStartInfo(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _podmanBin,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        return psi;
    }

    private static async Task ExtractTarGzAsync(byte[] tarGz, string destDir, CancellationToken ct)
    {
        await using var ms = new MemoryStream(tarGz, writable: false);
        await using var gz = new GZipStream(ms, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gz, destDir, overwriteFiles: true, ct);
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

    /// <summary>Esquema parcial del JSON de <c>podman ps --format=json</c>.</summary>
    private sealed class PodmanPsRow
    {
        public string? Id { get; set; }
        public List<string>? Names { get; set; }
        public string? Image { get; set; }
        public string? State { get; set; }
        public string? Status { get; set; }
        public List<PodmanPortRow>? Ports { get; set; }
    }

    private sealed class PodmanPortRow
    {
        public int ContainerPort { get; set; }
        public int HostPort { get; set; }
        public string? Protocol { get; set; }
    }
}
