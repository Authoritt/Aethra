using System.Security.Cryptography;
using System.Text;
using Aethra.Modules.Monitoring.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Monitoring.Infrastructure;

public static class MonitorUrlBackfill
{
    public static async Task<int> BackfillAsync(
        MonitoringDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var occupiedHashList = await db.Monitors
            .AsNoTracking()
            .Where(m => EF.Property<string?>(m, nameof(Monitor.NormalizedUrlHash)) != null)
            .Select(m => EF.Property<string>(m, nameof(Monitor.NormalizedUrlHash)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var occupiedHashes = new HashSet<string>(occupiedHashList, StringComparer.Ordinal);

        var pending = await db.Monitors
            .Where(m => EF.Property<string?>(m, nameof(Monitor.NormalizedUrlHash)) == null)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var monitor in pending)
        {
            var normalizedUrl = Monitor.NormalizeUrl(monitor.Url);
            var normalizedUrlHash = Monitor.HashNormalizedUrl(normalizedUrl);
            var persistenceKey = normalizedUrlHash;

            if (!occupiedHashes.Add(persistenceKey))
            {
                persistenceKey = CreateRemediationKey(monitor.Id.ToString());
                var attempt = 0;
                while (!occupiedHashes.Add(persistenceKey))
                {
                    persistenceKey = CreateRemediationKey($"{monitor.Id}:{++attempt}");
                }

                logger.LogWarning(
                    "El monitor {MonitorId} duplica la URL normalizada {NormalizedUrl}; se asignó la clave de remediación {RemediationKey}. Edite su URL para resolver el duplicado.",
                    monitor.Id,
                    normalizedUrl,
                    persistenceKey);
            }

            db.Entry(monitor).Property(m => m.NormalizedUrl).CurrentValue = normalizedUrl;
            db.Entry(monitor).Property(m => m.NormalizedUrlHash).CurrentValue = persistenceKey;
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return pending.Count;
    }

    private static string CreateRemediationKey(string value)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return $"dup-{hash[..60]}";
    }
}
