using Aethra.Modules.Projects.Domain;
using Aethra.Modules.Projects.UseCases.Dtos;

namespace Aethra.Modules.Projects.UseCases.Projects;

internal static class ProjectMapper
{
    public static ProjectDto ToDto(Project p) => new(
        Id: p.Id.ToString(),
        Slug: p.Slug.Value,
        Name: p.Name,
        Description: p.Description,
        Color: p.Color,
        Icon: p.Icon,
        CreatedAt: p.CreatedAt,
        UpdatedAt: p.UpdatedAt,
        Environments: [.. p.Environments.Select(ToDto)]);

    public static EnvironmentDto ToDto(Domain.Environment e) => new(
        Id: e.Id.ToString(),
        Name: e.Name,
        CreatedAt: e.CreatedAt,
        Applications: [.. e.Applications.Select(ToDto)]);

    public static ApplicationDto ToDto(Application a) => new(
        Id: a.Id.ToString(),
        Slug: a.Slug.Value,
        Name: a.Name,
        CreatedAt: a.CreatedAt,
        UpdatedAt: a.UpdatedAt,
        Source: new ApplicationSourceDto(
            GitRepoUrl: a.Source.GitRepoUrl.Value,
            Branch: a.Source.Branch,
            BaseDirectory: a.Source.BaseDirectory,
            WatchPaths: a.Source.WatchPaths,
            AccessTokenId: a.Source.AccessTokenId),
        Build: new ApplicationBuildDto(
            Type: a.Build.Type.ToString(),
            Path: a.Build.Path,
            Args: [.. a.Build.Args.Select(arg => new BuildArgDto(arg.Key, arg.Value))]),
        Runtime: new ApplicationRuntimeDto(
            TargetVmId: a.Runtime.TargetVmId,
            ContainerName: a.Runtime.ContainerName.Value,
            Ports: [.. a.Runtime.Ports.Select(p => new PortMappingDto(p.ContainerPort.Value, p.HostPort, p.Protocol))],
            Volumes: [.. a.Runtime.Volumes.Select(v => new VolumeMountDto(v.HostPath, v.ContainerPath, v.ReadOnly))]));
}
