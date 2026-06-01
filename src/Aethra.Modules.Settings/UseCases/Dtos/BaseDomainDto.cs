namespace Aethra.Modules.Settings.UseCases.Dtos;

public sealed record BaseDomainDto(
    string Id,
    string Hostname,
    string? CloudflareZoneId,
    bool WildcardConfigured,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
