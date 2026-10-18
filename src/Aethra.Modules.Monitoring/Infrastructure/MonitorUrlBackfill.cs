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
        var hasPending = await db.Monitors
            .AsNoTracking()
            .AnyAsync(
                m => EF.Property<string?>(m, nameof(Monitor.NormalizedUrlHash)) == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!hasPending)
        {
            return 0;
        }

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

            // Another instance may have completed the work while this one waited for the lock.
            db.ChangeTracker.Clear();
            var pending = await db.Monitors
                .Where(m => EF.Property<string?>(m, nameof(Monitor.NormalizedUrlHash)) == null)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var occupiedRows = await db.Monitors
                .AsNoTracking()
                .Where(m => EF.Property<string?>(m, nameof(Monitor.NormalizedUrlHash)) != null)
                .Select(m => new
                {
                    m.Id,
                    Hash = EF.Property<string>(m, nameof(Monitor.NormalizedUrlHash)),
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var occupiedHashes = occupiedRows.ToDictionary(
                row => row.Hash,
                row => row.Id,
                StringComparer.Ordinal);

            foreach (var monitor in pending)
            {
                var normalizedUrl = Monitor.NormalizeUrl(monitor.Url);
                var normalizedUrlHash = Monitor.HashNormalizedUrl(normalizedUrl);
                var persistenceKey = normalizedUrlHash;

                if (occupiedHashes.TryGetValue(persistenceKey, out var keptMonitorId))
                {
                    persistenceKey = CreateRemediationKey(monitor.Id.ToString());
                    var attempt = 0;
                    while (occupiedHashes.ContainsKey(persistenceKey))
                    {
                        persistenceKey = CreateRemediationKey($"{monitor.Id}:{++attempt}");
                    }

                    logger.LogWarning(
                        "Monitor {MonitorId} duplicates the normalized key retained by monitor {KeptMonitorId}; remediation key {RemediationKey} was assigned. Edit the monitor URL to resolve the duplicate.",
                        monitor.Id,
                        keptMonitorId,
                        persistenceKey);
                }

                occupiedHashes.Add(persistenceKey, monitor.Id);
                db.Entry(monitor).Property(m => m.NormalizedUrl).CurrentValue = normalizedUrl;
                db.Entry(monitor).Property(m => m.NormalizedUrlHash).CurrentValue = persistenceKey;
            }

            if (pending.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return pending.Count;
        }).ConfigureAwait(false);
    }

    private static string CreateRemediationKey(string value)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return $"dup-{hash[..60]}";
    }
}
