using Aethra.Modules.Identity.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Identity.Infrastructure.Conversions;

/// <summary>
/// Conversores EF Core para value-object IDs → string. Mismo patrón que los conversores
/// de Notes/Projects: helpers estáticos para evitar <c>out var</c> en expression trees.
/// </summary>
public static class ValueConverters
{
    public static readonly ValueConverter<ApiKeyId, string> ApiKeyIdConverter = new(
        id => id.ToString(),
        s => ParseApiKeyId(s));

    public static readonly ValueConverter<UserId, string> UserIdConverter = new(
        id => id.ToString(),
        s => ParseUserId(s));

    public static readonly ValueConverter<UserId?, string?> NullableUserIdConverter = new(
        id => id.HasValue ? id.Value.ToString() : null,
        s => string.IsNullOrEmpty(s) ? null : ParseUserId(s));

    public static readonly ValueConverter<RoleId, string> RoleIdConverter = new(
        id => id.ToString(),
        s => ParseRoleId(s));

    private static ApiKeyId ParseApiKeyId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ApiKeyId(parsed.Value) : default;

    private static UserId ParseUserId(string s)
        => AethraId.TryParse(s, out var parsed) ? new UserId(parsed.Value) : default;

    private static RoleId ParseRoleId(string s)
        => AethraId.TryParse(s, out var parsed) ? new RoleId(parsed.Value) : default;
}
