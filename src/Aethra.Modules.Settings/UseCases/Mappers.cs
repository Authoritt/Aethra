using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.UseCases.Dtos;

namespace Aethra.Modules.Settings.UseCases;

internal static class Mappers
{
    public static IntegrationCredentialDto ToDto(IntegrationCredential c) => new(
        Id: c.Id.ToString(),
        Name: c.Name,
        Type: c.Type,
        DisplayName: c.DisplayName,
        Description: c.Description,
        Metadata: c.Metadata,
        CreatedAt: c.CreatedAt,
        RotatedAt: c.RotatedAt,
        LastUsedAt: c.LastUsedAt);

    public static BaseDomainDto ToDto(BaseDomain d) => new(
        Id: d.Id.ToString(),
        Hostname: d.Hostname,
        CloudflareZoneId: d.CloudflareZoneId,
        WildcardConfigured: d.WildcardConfigured,
        IsActive: d.IsActive,
        CreatedAt: d.CreatedAt,
        UpdatedAt: d.UpdatedAt);

    public static EnvironmentDefinitionDto ToDto(EnvironmentDefinition e) => new(
        Id: e.Id.ToString(),
        Slug: e.Slug,
        DisplayName: e.DisplayName,
        Order: e.Order,
        CreatedAt: e.CreatedAt);
}
