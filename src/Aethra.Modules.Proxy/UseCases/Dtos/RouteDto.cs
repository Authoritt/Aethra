namespace Aethra.Modules.Proxy.UseCases.Dtos;

public sealed record RouteDto(
    string Id,
    string Hostname,
    string PathPrefix,
    string BackendUrl,
    bool TlsEnabled,
    string CertStatus,                  // "none" | "pending" | "issued" | "failed" | "renewing"
    DateTimeOffset? CertExpiresAt,
    string? OperationalOwnerType,
    string? OperationalOwnerId,
    string? Origin,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
