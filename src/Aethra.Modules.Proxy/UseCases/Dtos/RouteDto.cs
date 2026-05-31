namespace Aethra.Modules.Proxy.UseCases.Dtos;

public sealed record RouteDto(
    string Id,
    string Hostname,
    string BackendUrl,
    bool TlsEnabled,
    string CertStatus,                  // "none" | "pending" | "issued" | "failed" | "renewing"
    DateTimeOffset? CertExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
