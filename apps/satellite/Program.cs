using System.Runtime.InteropServices;
using Aethra.Satellite;
using Aethra.Satellite.Buffer;
using Aethra.Satellite.Docker;
using Aethra.Satellite.Probes;
using Aethra.Satellite.Workers;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((sp, lc) => lc.ReadFrom.Configuration(builder.Configuration));

builder.Services.Configure<SatelliteOptions>(opts =>
{
    opts.CentralUrl = Environment.GetEnvironmentVariable("AETHRA_CENTRAL_URL") ?? "http://localhost:5080";
    opts.Token = Environment.GetEnvironmentVariable("AETHRA_SATELLITE_TOKEN") ?? string.Empty;
    if (int.TryParse(Environment.GetEnvironmentVariable("AETHRA_METRICS_INTERVAL_SECONDS"), out var interval))
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

// Cliente Docker: si el socket/named-pipe está montado usamos Docker.DotNet;
// si no, fallback que loguea y devuelve "no disponible" (dev/tests sin Docker).
builder.Services.AddSingleton<IDockerClient>(sp =>
{
    var hasDockerSocket = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Directory.Exists(@"\\.\pipe\docker_engine")
        : File.Exists("/var/run/docker.sock");

    if (hasDockerSocket)
    {
        return new DockerDotNetClient(sp.GetRequiredService<ILogger<DockerDotNetClient>>());
    }

    var lg = sp.GetRequiredService<ILogger<DockerNotAvailableClient>>();
    lg.LogWarning("Socket Docker no detectado; usando DockerNotAvailableClient (modo dev/sin-docker).");
    return new DockerNotAvailableClient(lg);
});

builder.Services.AddSingleton<SatelliteCommandHandler>();

builder.Services.AddHostedService<SatelliteConnectionWorker>();

var host = builder.Build();
host.Run();
