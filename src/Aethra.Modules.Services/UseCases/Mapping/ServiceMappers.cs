using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Templates;
using Aethra.Modules.Services.UseCases.Dtos;

namespace Aethra.Modules.Services.UseCases.Mapping;

internal static class ServiceMappers
{
    public static ManagedServiceSummaryDto ToSummary(ManagedService s, int bindingsCount) => new(
        Id: s.Id.ToString(),
        Slug: s.Slug,
        Name: s.Name,
        Type: s.Type.ToString(),
        Version: s.Version,
        Status: s.Status.ToString().ToLowerInvariant(),
        TargetVmId: s.TargetVmId,
        ContainerName: s.ContainerName,
        BindingsCount: bindingsCount);

    public static ManagedServiceDetailDto ToDetail(ManagedService s, int bindingsCount) => new(
        Id: s.Id.ToString(),
        Slug: s.Slug,
        Name: s.Name,
        Type: s.Type.ToString(),
        Version: s.Version,
        Status: s.Status.ToString().ToLowerInvariant(),
        TargetVmId: s.TargetVmId,
        ContainerName: s.ContainerName,
        Image: s.Image,
        InternalPort: s.InternalPort,
        NetworkName: s.NetworkName,
        ExposedExternally: s.ExposedExternally,
        CreatedAt: s.CreatedAt,
        UpdatedAt: s.UpdatedAt,
        ProvisionedAt: s.ProvisionedAt,
        ErrorCode: s.ErrorCode,
        ErrorMessage: s.ErrorMessage,
        BindingsCount: bindingsCount);

    public static ServiceTemplateDto ToDto(ServiceTemplate t) => new(
        Id: t.Id,
        DisplayName: t.DisplayName,
        Type: t.Type.ToString(),
        Version: t.Version,
        Image: t.Image,
        InternalPort: t.InternalPort,
        Notes: t.Notes);

    public static ServiceBindingDto ToDto(ServiceBinding b, string? appSlug) => new(
        Id: b.Id.ToString(),
        ServiceId: b.ServiceId.ToString(),
        ApplicationId: b.ApplicationId,
        ApplicationSlug: appSlug,
        ResourceName: b.ResourceName,
        Permissions: b.Permissions.ToString().ToLowerInvariant(),
        EnvVarPrefix: b.InjectedEnvVarPrefix,
        HasMigrationsHook: b.MigrationsHook is not null,
        CreatedAt: b.CreatedAt,
        ProvisionedAt: b.ProvisionedAt,
        RevokedAt: b.RevokedAt,
        LastRotatedAt: b.LastRotatedAt);
}
