using Aethra.Modules.Services.Domain;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// Servicio scoped que orquesta una operacion de backup (o restore) end-to-end:
/// resuelve el engine por tipo, invoca el dump, escribe al storage segun el destination, y
/// persiste el <see cref="ServiceBackup"/> resultante con metadata. Reutilizable desde el
/// <see cref="BackupWorker"/> (cron) y desde los handlers de comandos on-demand.
/// </summary>
public sealed class BackupOrchestrator(
    ServicesDbContext db,
    IEnumerable<IBackupEngine> engines,
    IEnumerable<IBackupStorage> storages,
    IClock clock,
    ILogger<BackupOrchestrator> logger)
{
    public async Task<Result<ServiceBackup>> RunBackupAsync(ManagedServiceId serviceId, CancellationToken ct)
    {
        var svc = await db.ManagedServices
            .FirstOrDefaultAsync(s => s.Id == serviceId, ct)
            .ConfigureAwait(false);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", $"Servicio '{serviceId}' no existe.");
        }
        if (svc.BackupPolicy is null)
        {
            return Error.Validation("backup.no_policy", $"Servicio '{svc.Slug}' no tiene backup policy.");
        }

        var engine = engines.FirstOrDefault(e => e.Type == svc.Type);
        if (engine is null)
        {
            return Error.Validation("backup.no_engine", $"No hay engine para tipo {svc.Type}.");
        }

        var scheme = ExtractScheme(svc.BackupPolicy.Destination);
        var storage = storages.FirstOrDefault(s => s.Supports(scheme));
        if (storage is null)
        {
            return Error.Validation("backup.no_storage", $"No hay storage para esquema {scheme}://");
        }

        var fileName = $"{svc.Slug}-{clock.UtcNow:yyyyMMdd-HHmmss}.gz";
        var backup = ServiceBackup.Start(svc.Id, svc.BackupPolicy.Destination, clock.UtcNow);
        db.ServiceBackups.Add(backup);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        try
        {
            var bytes = await engine.CreateBackupAsync(svc, ct).ConfigureAwait(false);
            var finalPath = await storage.WriteAsync(svc.BackupPolicy.Destination, fileName, bytes, ct)
                .ConfigureAwait(false);

            // Reasignamos DestinationPath al path final con el filename via reflection-less helper.
            backup.UpdateDestinationPath(finalPath);
            backup.MarkCompleted(bytes.LongLength, clock.UtcNow);

            svc.MarkBackupCompleted(clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await EnforceRetentionAsync(svc, storage, ct).ConfigureAwait(false);
            logger.LogInformation("BackupOrchestrator: backup {Id} OK para {Slug}", backup.Id, svc.Slug);
            return backup;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "BackupOrchestrator: backup {Id} fallo para {Slug}", backup.Id, svc.Slug);
            backup.MarkFailed(ex.Message, clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Error.Failure("backup.failed", ex.Message);
        }
    }

    public async Task<Result> RunRestoreAsync(ManagedServiceId serviceId, ServiceBackupId backupId, CancellationToken ct)
    {
        var svc = await db.ManagedServices
            .FirstOrDefaultAsync(s => s.Id == serviceId, ct)
            .ConfigureAwait(false);
        if (svc is null)
        {
            return Error.NotFound("service.not_found", $"Servicio '{serviceId}' no existe.");
        }

        var backup = await db.ServiceBackups
            .FirstOrDefaultAsync(b => b.Id == backupId, ct)
            .ConfigureAwait(false);
        if (backup is null)
        {
            return Error.NotFound("backup.not_found", $"Backup '{backupId}' no existe.");
        }
        if (backup.ServiceId != serviceId)
        {
            return Error.Validation("backup.mismatch", "El backup no pertenece a este servicio.");
        }
        if (backup.Status != ServiceBackupStatus.Completed)
        {
            return Error.Validation("backup.not_completed", "Solo se puede restaurar un backup Completed.");
        }

        var engine = engines.FirstOrDefault(e => e.Type == svc.Type);
        if (engine is null)
        {
            return Error.Validation("backup.no_engine", $"No hay engine para tipo {svc.Type}.");
        }

        var scheme = ExtractScheme(backup.DestinationPath);
        var storage = storages.FirstOrDefault(s => s.Supports(scheme));
        if (storage is null)
        {
            return Error.Validation("backup.no_storage", $"No hay storage para esquema {scheme}://");
        }

        try
        {
            var bytes = await storage.ReadAsync(backup.DestinationPath, ct).ConfigureAwait(false);
            await engine.RestoreBackupAsync(svc, bytes, ct).ConfigureAwait(false);
            svc.MarkRestored(clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("BackupOrchestrator: restore {BackupId} OK sobre {Slug}", backup.Id, svc.Slug);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "BackupOrchestrator: restore {BackupId} fallo", backup.Id);
            return Error.Failure("restore.failed", ex.Message);
        }
    }

    public async Task<Result> DeleteBackupAsync(ServiceBackupId backupId, CancellationToken ct)
    {
        var backup = await db.ServiceBackups.FirstOrDefaultAsync(b => b.Id == backupId, ct)
            .ConfigureAwait(false);
        if (backup is null)
        {
            return Error.NotFound("backup.not_found", $"Backup '{backupId}' no existe.");
        }

        var scheme = ExtractScheme(backup.DestinationPath);
        var storage = storages.FirstOrDefault(s => s.Supports(scheme));
        if (storage is not null && backup.Status == ServiceBackupStatus.Completed)
        {
            try
            {
                await storage.DeleteAsync(backup.DestinationPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "BackupOrchestrator: borrado fisico fallo para {Id}", backup.Id);
            }
        }

        db.ServiceBackups.Remove(backup);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task EnforceRetentionAsync(ManagedService svc, IBackupStorage storage, CancellationToken ct)
    {
        if (svc.BackupPolicy is null) { return; }
        var keep = Math.Max(1, svc.BackupPolicy.RetentionCount);
        var allCompleted = await db.ServiceBackups
            .Where(b => b.ServiceId == svc.Id && b.Status == ServiceBackupStatus.Completed)
            .OrderByDescending(b => b.StartedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (allCompleted.Count <= keep) { return; }

        var toDelete = allCompleted.Skip(keep).ToList();
        foreach (var old in toDelete)
        {
            try
            {
                await storage.DeleteAsync(old.DestinationPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Retention: no se pudo borrar {Path}", old.DestinationPath);
            }
            db.ServiceBackups.Remove(old);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation(
            "BackupOrchestrator: retention dejo {Keep} backups, borro {Deleted} para {Slug}",
            keep, toDelete.Count, svc.Slug);
    }

    private static string ExtractScheme(string url)
    {
        var idx = url.IndexOf("://", StringComparison.Ordinal);
        return idx <= 0 ? "volume" : url[..idx];
    }
}
