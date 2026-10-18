using System.Security.Cryptography;
using System.Text;
using Aethra.Modules.Monitoring.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Monitoring.Infrastructure;

public static class MonitorUrlBackfill
{
    public const string RequiredMigration = "20261018000000_UniqueNormalizedMonitorUrl";

    // ASCII "AETHRA", followed by the issue number, as a stable PostgreSQL advisory-lock key.
    private const long AdvisoryLockKey = 0x4145544852410032;

    public static async Task<int> BackfillIfMigrationAppliedAsync(
        MonitoringDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var appliedMigrations = await db.Database
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!appliedMigrations.Contains(RequiredMigration, StringComparer.Ordinal))
        {
            logger.LogInformation(
                "Monitor URL backfill skipped because migration {Migration} has not been applied.",
                RequiredMigration);
            return 0;
        }

        return await BackfillAsync(db, logger, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> BackfillAsync(
        MonitoringDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})",
                    cancellationToken)
                .ConfigureAwait(false);

            // Another instance may have reconciled the rows while this one waited for the lock.
            db.ChangeTracker.Clear();
            var monitors = await db.Monitors
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var reconciliations = new List<Reconciliation>(monitors.Count);
            var retainedByHash = new Dictionary<string, MonitorId>(StringComparer.Ordinal);
            foreach (var monitor in monitors)
            {
                var normalizedUrl = Monitor.NormalizeUrl(monitor.Url);
                var normalizedUrlHash = Monitor.HashNormalizedUrl(normalizedUrl);
                var isDuplicate = retainedByHash.TryGetValue(normalizedUrlHash, out var keptMonitorId);
                if (!isDuplicate)
                {
                    retainedByHash.Add(normalizedUrlHash, monitor.Id);
                }

                reconciliations.Add(new Reconciliation(
                    monitor,
                    normalizedUrl,
                    normalizedUrlHash,
                    isDuplicate ? keptMonitorId : null));
            }

            var occupiedKeys = reconciliations
                .Where(item => item.KeptMonitorId is null || IsRemediationKey(item.Monitor.NormalizedUrlHash))
                .Select(item => item.KeptMonitorId is null
                    ? item.NormalizedUrlHash
                    : item.Monitor.NormalizedUrlHash)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var item in reconciliations.Where(item => item.KeptMonitorId is not null))
            {
                if (IsRemediationKey(item.Monitor.NormalizedUrlHash))
                {
                    item.PersistenceKey = item.Monitor.NormalizedUrlHash;
                    continue;
                }

                var attempt = 0;
                var persistenceKey = CreateRemediationKey(item.Monitor.Id.ToString());
                while (!occupiedKeys.Add(persistenceKey))
                {
                    persistenceKey = CreateRemediationKey($"{item.Monitor.Id}:{++attempt}");
                }
                item.PersistenceKey = persistenceKey;
            }

            var changed = reconciliations
                .Where(item => !string.Equals(
                        item.Monitor.NormalizedUrl,
                        item.NormalizedUrl,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        item.Monitor.NormalizedUrlHash,
                        item.PersistenceKey,
                        StringComparison.Ordinal))
                .ToList();

            // Release obsolete unique keys first so a real key can safely move to the oldest row.
            foreach (var item in changed.Where(item =>
                         !string.Equals(
                             item.Monitor.NormalizedUrlHash,
                             item.PersistenceKey,
                             StringComparison.Ordinal)
                         && item.Monitor.NormalizedUrlHash is not null))
            {
                db.Entry(item.Monitor).Property(m => m.NormalizedUrlHash).CurrentValue = null!;
            }
            if (db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var item in changed)
            {
                db.Entry(item.Monitor).Property(m => m.NormalizedUrl).CurrentValue = item.NormalizedUrl;
                db.Entry(item.Monitor).Property(m => m.NormalizedUrlHash).CurrentValue = item.PersistenceKey;

                if (item.KeptMonitorId is { } keptMonitorId)
                {
                    logger.LogWarning(
                        "Monitor {MonitorId} duplicates the normalized key retained by monitor {KeptMonitorId}; edit the monitor URL to resolve the duplicate.",
                        item.Monitor.Id,
                        keptMonitorId);
                }
            }
            if (db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return changed.Count;
        }).ConfigureAwait(false);
    }

    private static bool IsRemediationKey(string? value)
        => value?.StartsWith("dup-", StringComparison.Ordinal) == true;

    private static string CreateRemediationKey(string value)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return $"dup-{hash[..60]}";
    }

    private sealed class Reconciliation(
        Monitor monitor,
        string normalizedUrl,
        string normalizedUrlHash,
        MonitorId? keptMonitorId)
    {
        public Monitor Monitor { get; } = monitor;
        public string NormalizedUrl { get; } = normalizedUrl;
        public string NormalizedUrlHash { get; } = normalizedUrlHash;
        public MonitorId? KeptMonitorId { get; } = keptMonitorId;
        public string PersistenceKey { get; set; } = normalizedUrlHash;
    }
}
