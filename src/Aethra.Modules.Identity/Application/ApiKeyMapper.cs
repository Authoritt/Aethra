using Aethra.Modules.Identity.Application.Dtos;
using Aethra.Modules.Identity.Domain;

namespace Aethra.Modules.Identity.Application;

internal static class ApiKeyMapper
{
    public static ApiKeySummaryDto ToSummary(ApiKey key) => new(
        Id: key.Id.ToString(),
        Name: key.Name,
        KeyPrefix: key.KeyPrefix,
        Scopes: [.. key.Scopes],
        CreatedAt: key.CreatedAt,
        LastUsedAt: key.LastUsedAt,
        RevokedAt: key.RevokedAt,
        ExpiresAt: key.ExpiresAt);
}
