using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Shared.Contracts.Projects;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.Infrastructure.Lookups;

/// <summary>
/// OT-009 (G2) — implementación EF de <see cref="IHostnameUsageLookup"/>: instancias que declaran un
/// host como suyo (CustomDomain, AutoHostname o override por servicio). Cardinalidad baja: se
/// materializa y filtra en memoria (mismo criterio que <see cref="EfInstanceLookup"/>).
/// </summary>
internal sealed class EfHostnameUsageLookup(ProjectsDbContext db) : IHostnameUsageLookup
{
    public async Task<IReadOnlyList<string>> FindInstancesUsingHostnameAsync(
        string hostname, string? excludingInstanceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return [];
        }
        var instances = await db.Instances.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return [.. instances
            .Where(i => !string.Equals(i.Id.ToString(), excludingInstanceId, StringComparison.Ordinal)
                && UsesHostname(i, hostname.Trim()))
            .Select(i => i.Id.ToString())];
    }

    internal static bool UsesHostname(Instance instance, string hostname)
        => string.Equals(instance.CustomDomain, hostname, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.AutoHostname, hostname, StringComparison.OrdinalIgnoreCase)
            || instance.ServiceHostnames.Any(o => string.Equals(o.Hostname, hostname, StringComparison.OrdinalIgnoreCase));
}
