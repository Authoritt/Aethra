using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Aethra.Shared.Contracts.Deployments;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Aethra.Satellite.Docker;

/// <summary>
/// Implementación de <see cref="IDockerClient"/> sobre <c>Docker.DotNet</c>.
/// Habla contra el socket Docker local del host: Unix socket en Linux,
/// named pipe en Windows.
/// </summary>
public sealed class DockerDotNetClient : IDockerClient, IDisposable
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerDotNetClient> _logger;

    public DockerDotNetClient(ILogger<DockerDotNetClient> logger)
    {
        _logger = logger;
        var endpoint = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");
        _client = new DockerClientConfiguration(endpoint).CreateClient();
    }

    /// <summary>
    /// Construye una imagen. El central comprime el build context como tarball y
    /// lo serializa en Base64; aquí lo decodificamos y lo pasamos como Stream
    /// al cliente Docker. Los progress messages se acumulan en una lista para
    /// retornarlos junto al resultado (útil para diagnóstico de builds fallidos).
    /// </summary>
    public async Task<BuildImageResult> BuildImageAsync(BuildImageRequest request, CancellationToken ct)
    {
        var logs = new List<string>();
        try
        {
            var tarballBytes = Convert.FromBase64String(request.ContextTarballBase64);
            await using var tarball = new MemoryStream(tarballBytes, writable: false);

            var parameters = new ImageBuildParameters
            {
                Dockerfile = request.DockerfileRelativePath,
                Tags = [request.ImageTag],
                BuildArgs = request.BuildArgs.ToDictionary(kv => kv.Key, kv => kv.Value),
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
                    logs.Add($"ERROR: {err}");
                }
                if (msg.Aux is not null)
                {
                    // Aux es un JToken-ish; en 3.125.x es de tipo dynamic via Newtonsoft.
                    // El payload típico es {"ID":"sha256:..."}. Lo serializamos a string y parseamos.
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
                return new BuildImageResult(request.BuildJobId, Success: false, ImageId: null, errorDetail, logs);
            }

            return new BuildImageResult(request.BuildJobId, Success: true, imageId, ErrorMessage: null, logs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build de imagen {Tag} falló (job={Job})", request.ImageTag, request.BuildJobId);
            return new BuildImageResult(
                request.BuildJobId, Success: false, ImageId: null, ErrorMessage: ex.Message, logs);
        }
    }

    /// <summary>
    /// Crea + arranca un contenedor. Si la imagen no está local, hace pull primero.
    /// </summary>
    public async Task<RunContainerResult> RunContainerAsync(RunContainerRequest request, CancellationToken ct)
    {
        try
        {
            await EnsureImageAvailableAsync(request.ImageRef, ct);

            var envList = request.EnvVars.Select(kv => $"{kv.Key}={kv.Value}").ToList();

            // Mapeo de puertos: ExposedPorts declara el puerto del contenedor; HostConfig.PortBindings
            // ata cada uno al host. Si HostPort es null, lo dejamos vacío y Docker asigna ephemeral.
            var exposedPorts = new Dictionary<string, EmptyStruct>(StringComparer.Ordinal);
            var portBindings = new Dictionary<string, IList<PortBinding>>(StringComparer.Ordinal);
            foreach (var p in request.Ports)
            {
                var key = $"{p.ContainerPort}/{p.Protocol.ToLowerInvariant()}";
                exposedPorts[key] = default;
                portBindings[key] = new List<PortBinding>
                {
                    new() { HostPort = p.HostPort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty },
                };
            }

            var binds = request.Volumes
                .Select(v => $"{v.Source}:{v.Target}{(v.ReadOnly ? ":ro" : string.Empty)}")
                .ToList();

            var createParams = new CreateContainerParameters
            {
                Name = request.ContainerName,
                Image = request.ImageRef,
                Env = envList,
                ExposedPorts = exposedPorts,
                HostConfig = new HostConfig
                {
                    PortBindings = portBindings,
                    Binds = binds,
                    NetworkMode = request.NetworkName,
                },
            };

            if (request.Healthcheck is { } hc)
            {
                createParams.Healthcheck = new HealthConfig
                {
                    Test = hc.Cmd.ToList(),
                    Interval = hc.Interval,
                    Timeout = hc.Timeout,
                    Retries = hc.Retries,
                };
            }

            var created = await _client.Containers.CreateContainerAsync(createParams, ct);
            var started = await _client.Containers.StartContainerAsync(
                created.ID, new ContainerStartParameters(), ct);
            if (!started)
            {
                return new RunContainerResult(
                    request.DeployJobId, Success: false, ContainerId: created.ID,
                    ErrorMessage: "Docker reportó que el contenedor no arrancó");
            }
            return new RunContainerResult(request.DeployJobId, Success: true, created.ID, ErrorMessage: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run de contenedor {Name} falló (job={Job})",
                request.ContainerName, request.DeployJobId);
            return new RunContainerResult(
                request.DeployJobId, Success: false, ContainerId: null, ErrorMessage: ex.Message);
        }
    }

    public async Task StopContainerAsync(StopContainerRequest request, CancellationToken ct)
    {
        var waitSeconds = (uint)Math.Max(0, request.Timeout.TotalSeconds);
        await _client.Containers.StopContainerAsync(
            request.ContainerName,
            new ContainerStopParameters { WaitBeforeKillSeconds = waitSeconds },
            ct);
    }

    public async Task RemoveContainerAsync(RemoveContainerRequest request, CancellationToken ct)
    {
        await _client.Containers.RemoveContainerAsync(
            request.ContainerName,
            new ContainerRemoveParameters { Force = request.Force, RemoveVolumes = false },
            ct);
    }

    /// <summary>
    /// Streamea logs en formato multiplexado. Docker antepone un header de 8 bytes por frame:
    /// <c>[StreamType, 0,0,0, BigEndianSizeUInt32]</c> donde StreamType ∈ {0=stdin,1=stdout,2=stderr}.
    /// Docker.DotNet expone <c>MultiplexedStream.ReadOutputAsync</c> que devuelve un <c>ReadResult</c>
    /// con Target ya resuelto (StandardOut/StandardError), evitando que tengamos que parsear el header
    /// a mano. Si el daemon corre con tty=true los frames vienen sin header — DDN lo maneja interno.
    /// </summary>
    public async IAsyncEnumerable<LogChunk> StreamLogsAsync(
        StreamLogsRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var parameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Follow = request.Follow,
            Tail = request.TailLines > 0 ? request.TailLines.ToString(CultureInfo.InvariantCulture) : "all",
        };

        // GetContainerLogsAsync(name, tty:false, parameters, ct) → MultiplexedStream.
        // tty:false ya que asumimos contenedores sin TTY (worker/serverless típicos).
        using var stream = await _client.Containers.GetContainerLogsAsync(
            request.ContainerName, tty: false, parameters, ct);

        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
                if (result.EOF)
                {
                    yield break;
                }
                if (result.Count <= 0)
                {
                    continue;
                }
                var streamName = result.Target == MultiplexedStream.TargetStream.StandardError
                    ? "stderr" : "stdout";
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                yield return new LogChunk(request.ContainerName, streamName, text);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken ct)
    {
        var raw = await _client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true }, ct);
        return raw.Select(c => new ContainerSummary(
            Id: c.ID,
            // Docker prefija los nombres con "/". Lo quitamos para que el central reciba el nombre limpio.
            Name: c.Names is { Count: > 0 } ? c.Names[0].TrimStart('/') : string.Empty,
            Image: c.Image ?? string.Empty,
            State: c.State ?? string.Empty,
            Status: c.Status ?? string.Empty)).ToList();
    }

    /// <summary>
    /// Pull de la imagen si no está local. Docker.DotNet no tiene una API "exists" directa,
    /// pero <c>InspectImageAsync</c> lanza <c>DockerImageNotFoundException</c> si falta.
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
            // Caemos al pull.
        }

        var (fromImage, tag) = SplitImageRef(imageRef);
        await _client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = fromImage, Tag = tag },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            ct);
    }

    private static (string FromImage, string Tag) SplitImageRef(string imageRef)
    {
        // Si tiene digest (@sha256:...) lo pasamos completo como FromImage sin tag.
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
