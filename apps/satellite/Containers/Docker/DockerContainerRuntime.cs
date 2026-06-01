using System.Buffers;
using System.Globalization;
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
    /// </summary>
    public async Task<BuildResult> BuildImageAsync(BuildSpec spec, CancellationToken ct)
    {
        var logs = new List<string>();
        try
        {
            await using var tarball = new MemoryStream(spec.BuildContextTarGz, writable: false);

            var parameters = new ImageBuildParameters
            {
                Dockerfile = spec.DockerfilePath,
                Tags = [spec.ImageRef],
                BuildArgs = spec.BuildArgs.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            };

            // Docker.DotNet emite mensajes JSON tipo {stream, errorDetail, aux:{ID}}.
            // ID en aux suele venir al final con el sha256 de la imagen construida.
            string? imageId = null;
            string? errorDetail = null;
            var progress = new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrWhiteSpace(msg.Stream))
                {
                    logs.Add(msg.Stream.TrimEnd('\n', '\r'));
                }
                if (msg.ErrorMessage is { Length: > 0 } err)
                {
                    errorDetail = err;
                    logs.Add(string.Create(CultureInfo.InvariantCulture, $"ERROR: {err}"));
                }
                if (msg.Aux is not null)
                {
                    // Aux viene como dynamic JToken; el payload típico es {"ID":"sha256:..."}.
                    var auxText = msg.Aux.ToString();
                    if (auxText is not null)
                    {
                        const string idKey = "\"ID\":\"";
                        var idx = auxText.IndexOf(idKey, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            var start = idx + idKey.Length;
                            var end = auxText.IndexOf('"', start);
                            if (end > start)
                            {
                                imageId = auxText[start..end];
                            }
                        }
                    }
                }
            });

            await _client.Images.BuildImageFromDockerfileAsync(
                parameters, tarball, authConfigs: null, headers: null, progress, ct);

            if (errorDetail is not null)
            {
                return new BuildResult(Success: false, ImageId: null, errorDetail, logs);
            }

            return new BuildResult(Success: true, imageId, ErrorMessage: null, logs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build de imagen {ImageRef} falló", spec.ImageRef);
            return new BuildResult(Success: false, ImageId: null, ErrorMessage: ex.Message, logs);
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
            var exposedPorts = new Dictionary<string, EmptyStruct>(StringComparer.Ordinal);
            var portBindings = new Dictionary<string, IList<global::Docker.DotNet.Models.PortBinding>>(StringComparer.Ordinal);
            foreach (var p in spec.Ports)
            {
                var key = string.Create(CultureInfo.InvariantCulture, $"{p.ContainerPort}/{p.Protocol.ToLowerInvariant()}");
                exposedPorts[key] = default;
                portBindings[key] = new List<global::Docker.DotNet.Models.PortBinding>
                {
                    new() { HostPort = p.HostPort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty },
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

    public async Task RemoveContainerAsync(string nameOrId, bool force, CancellationToken ct)
    {
        await _client.Containers.RemoveContainerAsync(
            nameOrId,
            new ContainerRemoveParameters { Force = force, RemoveVolumes = false },
            ct);
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

    public void Dispose() => _client.Dispose();
}
