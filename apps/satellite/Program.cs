using System.Globalization;
using System.Runtime.InteropServices;
using Aethra.Satellite;
using Aethra.Satellite.Buffer;
using Aethra.Satellite.Containers;
using Aethra.Satellite.Containers.Docker;
using Aethra.Satellite.Containers.Podman;
using Aethra.Satellite.Probes;
using Aethra.Satellite.Workers;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((sp, lc) => lc.ReadFrom.Configuration(builder.Configuration));

builder.Services.Configure<SatelliteOptions>(opts =>
{
    opts.CentralUrl = Environment.GetEnvironmentVariable("AETHRA_CENTRAL_URL") ?? "http://localhost:5080";
    opts.Token = Environment.GetEnvironmentVariable("AETHRA_SATELLITE_TOKEN") ?? string.Empty;
    if (int.TryParse(
            Environment.GetEnvironmentVariable("AETHRA_METRICS_INTERVAL_SECONDS"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var interval))
    {
        opts.MetricsIntervalSeconds = interval;
    }
    opts.BufferPath = Environment.GetEnvironmentVariable("AETHRA_SATELLITE_BUFFER_PATH");
});

// Elegimos probe según OS. Linux → /proc real; otros → BCL cross-platform (Windows dev).
builder.Services.AddSingleton<IMetricsProbe>(sp =>
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Directory.Exists("/proc"))
    {
        return new LinuxMetricsProbe();
    }
    return new CrossPlatformMetricsProbe();
});

// Buffer persistente local para snapshots cuando el central no es alcanzable
// (patrón "replication" de Netdata).
builder.Services.AddSingleton<ISnapshotBuffer, SqliteSnapshotBuffer>();

// Container runtime: selector configurable Satellite:ContainerRuntime = "docker" | "podman".
// docker → Docker.DotNet contra el socket local (unix o named pipe).
// podman → wrapper sobre el CLI podman.
var runtimeKind = (builder.Configuration["Satellite:ContainerRuntime"] ?? "docker").ToLowerInvariant();
switch (runtimeKind)
{
    case "docker":
        builder.Services.AddSingleton<IContainerRuntime, DockerContainerRuntime>();
        break;
    case "podman":
        builder.Services.Configure<PodmanOptions>(builder.Configuration.GetSection("Satellite:Podman"));
        builder.Services.AddSingleton<IContainerRuntime, PodmanContainerRuntime>();
        break;
    default:
        throw new InvalidOperationException(
            $"Container runtime no soportado: '{runtimeKind}'. Valores válidos: 'docker', 'podman'.");
}

builder.Services.AddSingleton<SatelliteCommandHandler>();

builder.Services.AddHostedService<SatelliteConnectionWorker>();

var host = builder.Build();
host.Run();
