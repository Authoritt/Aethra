using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using Aethra.Shared.Kernel.Errors;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aethra.Modules.Monitoring.UseCases.Commands;

internal static class MonitorUrlUniqueness
{
    internal const string ConstraintName = "ux_monitors_normalized_url";

    internal static Task<bool> IsTakenAsync(
        MonitoringDbContext db,
        string url,
        MonitorId? excludedMonitorId,
        CancellationToken ct)
        => db.Monitors.AnyAsync(BuildConflictPredicate(url, excludedMonitorId), ct);

    internal static Expression<Func<Monitor, bool>> BuildConflictPredicate(
        string url,
        MonitorId? excludedMonitorId)
    {
        var normalizedUrl = Monitor.NormalizeUrl(url);

        return monitor => monitor.NormalizedUrl == normalizedUrl
            && (!excludedMonitorId.HasValue || monitor.Id != excludedMonitorId.Value);
    }

    internal static Error Conflict(string url)
        => Error.Conflict(
            "monitor.url_taken",
            $"Ya existe un monitor para la URL '{url.Trim()}'. Editá el existente en vez de duplicarlo.");

    internal static bool IsUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ConstraintName,
        };

}
