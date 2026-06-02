using Aethra.Modules.Services.Domain;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// BackgroundService que dispara backups segun la cron de cada <see cref="ManagedService.BackupPolicy"/>.
/// Poll cada 60s. La logica de "toca disparar?" es: si <see cref="ManagedService.LastBackupAt"/> es
/// null OR mayor a 1h atras Y matchea la cron mas reciente, entonces backup ahora.
///
/// La evaluacion cron es minimalista (lee primer campo en minutos): patron <c>*/N</c> o entero.
/// Para cron expressions complejas el operador puede invocar el endpoint <c>POST /backups/run</c>
/// directamente desde un cron externo.
/// </summary>
public sealed class BackupWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<BackupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BackupWorker arrancando (poll cada {Seconds}s)", PollInterval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "BackupWorker: fallo loop");
            }
            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServicesDbContext>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<BackupOrchestrator>();

        var services = await db.ManagedServices
            .Where(s => s.Status == ManagedServiceStatus.Ready)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        foreach (var svc in services)
        {
            if (svc.BackupPolicy is null) { continue; }
            if (!ShouldRunBackup(svc, now)) { continue; }

            logger.LogInformation("BackupWorker: ejecutando backup programado para {Slug}", svc.Slug);
            try
            {
                await orchestrator.RunBackupAsync(svc.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BackupWorker: backup {Slug} fallo", svc.Slug);
            }
        }
    }

    private static bool ShouldRunBackup(ManagedService svc, DateTimeOffset now)
    {
        if (svc.BackupPolicy is null) { return false; }

        var intervalMinutes = ParseIntervalMinutes(svc.BackupPolicy.CronExpression);
        if (intervalMinutes <= 0) { return false; }

        if (svc.LastBackupAt is null) { return true; }
        return (now - svc.LastBackupAt.Value).TotalMinutes >= intervalMinutes;
    }

    private static int ParseIntervalMinutes(string cron)
    {
        // Soportamos patrones simples: "*/N" o "N" o "0 * * * *" (cualquier minute = 60min).
        // Para algo mas potente integrar NCrontab en el futuro.
        var trimmed = cron.Trim();
        var firstField = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        if (firstField.StartsWith("*/", StringComparison.Ordinal)
            && int.TryParse(firstField[2..], out var stepMin))
        {
            return Math.Max(1, stepMin);
        }
        if (firstField == "*") { return 60; }
        if (int.TryParse(firstField, out _))
        {
            // Field es "N" → corre 1 vez por hora a ese minuto. Aproximamos a 60min.
            return 60;
        }
        // Default: cada 24h.
        return 1440;
    }
}
