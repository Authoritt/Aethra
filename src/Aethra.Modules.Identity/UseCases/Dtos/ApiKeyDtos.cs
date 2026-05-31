namespace Aethra.Modules.Identity.UseCases.Dtos;

public sealed record ApiKeySummaryDto(
    string Id,
    string Name,
    string KeyPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

public sealed record CreateApiKeyResultDto(
    string Id,
    string Name,
    string KeyPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string Secret);
