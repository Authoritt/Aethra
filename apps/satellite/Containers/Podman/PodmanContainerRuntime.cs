using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aethra.Shared.Contracts.Containers;
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
                args.Add(p.HostPort is int hp
                    ? string.Create(CultureInfo.InvariantCulture, $"{hp}:{p.ContainerPort}/{p.Protocol.ToLowerInvariant()}")
                    : string.Create(CultureInfo.InvariantCulture, $"{p.ContainerPort}/{p.Protocol.ToLowerInvariant()}"));
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
