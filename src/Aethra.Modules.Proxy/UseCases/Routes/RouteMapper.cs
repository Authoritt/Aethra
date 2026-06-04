using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.UseCases.Dtos;

namespace Aethra.Modules.Proxy.UseCases.Routes;

internal static class RouteMapper
{
    public static RouteDto ToDto(Route r, string certStatus, DateTimeOffset? certExpiresAt) => new(
        Id: r.Id.ToString(),
        Hostname: r.Hostname.Value,
        PathPrefix: r.PathPrefix,
        BackendUrl: r.BackendUrl,
        TlsEnabled: r.TlsEnabled,
        CertStatus: certStatus,
        CertExpiresAt: certExpiresAt,
        CreatedAt: r.CreatedAt,
        UpdatedAt: r.UpdatedAt);
}
