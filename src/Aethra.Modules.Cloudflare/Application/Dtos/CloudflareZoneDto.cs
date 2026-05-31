namespace Aethra.Modules.Cloudflare.Application.Dtos;

/// <summary>
/// Vista resumida de una zona para listados.
/// </summary>
public sealed record CloudflareZoneDto(
    string Id,
    string ExternalZoneId,
    string Name,
    string Status,
    string AccountId,
    int RecordsCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSyncedAt);

/// <summary>
/// Vista detallada que incluye los registros DNS asociados.
/// </summary>
public sealed record CloudflareZoneDetailDto(
    string Id,
    string ExternalZoneId,
    string Name,
    string Status,
    string AccountId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSyncedAt,
    IReadOnlyList<DnsRecordDto> Records);

/// <summary>
/// Vista de un record DNS para listados y respuestas de comando.
/// </summary>
public sealed record DnsRecordDto(
    string Id,
    string ZoneId,
    string? ExternalRecordId,
    string Type,
    string Name,
    string Content,
    int Ttl,
    bool Proxied,
    string? Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SyncedAt,
    string? LastError);
