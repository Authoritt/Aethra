using Aethra.Satellite.Containers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Satellite.Workers;

/// <summary>
/// Backstop periódico contra la fuga de disco por deploys. El prune post-build del
/// <see cref="SatelliteCommandHandler"/> sólo corre cuando hay un build git-mode; este worker corre
/// cada <see cref="SatelliteOptions.DiskJanitorIntervalHours"/> horas INDEPENDIENTEMENTE de los builds,
/// cubriendo dos huecos que hacían que el disco se volviera a llenar:
/// <list type="bullet">
/// <item>Ráfagas/idle: si los builds paran, el cache y los colgantes no se reclamarían hasta el
/// próximo build (que puede no llegar).</item>
/// <item>Builds que NO pasan por el satélite — p.ej. el rebuild manual del central
/// (<c>aethra-central</c>) genera build cache que ningún hook post-build poda.</item>
/// </list>
/// Acota el build cache al tope de tamaño (<see cref="SatelliteOptions.BuildCacheKeepStorageGb"/>),
/// borra imágenes colgantes y poda volúmenes anónimos colgantes (hash 64-hex, nunca named volumes de
/// datos). Best-effort: nunca toca imágenes con tag ni contenedores/volúmenes en uso, y
/// cualquier fallo se loguea sin tumbar el satélite. La retención de tags por repo sigue en el hook
/// post-build (keep-last-N), que es donde se conoce el repo recién construido.
/// </summary>
public sealed class DiskJanitorWorker(
    IContainerRuntime runtime,
    IOptions<SatelliteOptions> options,
    ILogger<DiskJanitorWorker> logger) : BackgroundService
{
    private readonly int _intervalHours = options.Value.DiskJanitorIntervalHours;
    private readonly int _keepStorageGb = options.Value.BuildCacheKeepStorageGb;
    private readonly int _maxAgeHours = options.Value.BuildCacheMaxAgeHours;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_intervalHours <= 0)
        {
            logger.LogInformation("DiskJanitor desactivado (DiskJanitorIntervalHours <= 0).");
            return;
        }

        var interval = TimeSpan.FromHours(_intervalHours);
        // Primera pasada tras un breve arranque (deja que el handshake/registro terminen primero).
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        do
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            var cache = await runtime.PruneBuildCacheAsync(_maxAgeHours, _keepStorageGb, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(cache))
            {
                logger.LogInformation("DiskJanitor — build cache: {Summary}", cache);
            }

            var dangling = await runtime.PruneDanglingImagesAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(dangling))
            {
                logger.LogInformation("DiskJanitor — {Summary}", dangling);
            }

            var volumes = await runtime.PruneAnonymousVolumesAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(volumes))
            {
                logger.LogInformation("DiskJanitor — {Summary}", volumes);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "DiskJanitor: pasada de limpieza falló (se reintenta en el próximo ciclo).");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
