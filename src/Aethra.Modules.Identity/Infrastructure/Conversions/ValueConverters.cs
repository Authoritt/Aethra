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

    private static ApiKeyId ParseApiKeyId(string s)
        => AethraId.TryParse(s, out var parsed) ? new ApiKeyId(parsed.Value) : default;
}
