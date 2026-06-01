using Aethra.Modules.Settings.Domain;
using Aethra.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aethra.Modules.Settings.Infrastructure.Conversions;

/// <summary>
/// Conversores EF Core para value-object IDs → string. Mismo patrón que los conversores
/// del resto de módulos: helpers estáticos para evitar <c>out var</c> en expression trees.
/// </summary>
public static class ValueConverters
{
    public static readonly ValueConverter<IntegrationCredentialId, string> IntegrationCredentialIdConverter = new(
        id => id.ToString(),
        s => ParseCredentialId(s));

    public static readonly ValueConverter<BaseDomainId, string> BaseDomainIdConverter = new(
        id => id.ToString(),
        s => ParseBaseDomainId(s));

    public static readonly ValueConverter<EnvironmentDefinitionId, string> EnvironmentDefinitionIdConverter = new(
        id => id.ToString(),
        s => ParseEnvironmentDefinitionId(s));

    private static IntegrationCredentialId ParseCredentialId(string s)
        => AethraId.TryParse(s, out var parsed) ? new IntegrationCredentialId(parsed.Value) : default;

    private static BaseDomainId ParseBaseDomainId(string s)
        => AethraId.TryParse(s, out var parsed) ? new BaseDomainId(parsed.Value) : default;

    private static EnvironmentDefinitionId ParseEnvironmentDefinitionId(string s)
        => AethraId.TryParse(s, out var parsed) ? new EnvironmentDefinitionId(parsed.Value) : default;
}
