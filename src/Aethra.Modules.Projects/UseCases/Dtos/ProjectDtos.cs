namespace Aethra.Modules.Projects.UseCases.Dtos;

/// <summary>
/// DTOs de lectura — superficie estable de la API. Convención snake_case en JSON
/// (configurado en el host) ↔ PascalCase en C#.
/// </summary>
public sealed record ProjectDto(
    string Id,
    string Slug,
    string Name,
    string? Description,
    string? Color,
    string? Icon,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<EnvironmentDto> Environments);

public sealed record EnvironmentDto(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ApplicationDto> Applications);

public sealed record ApplicationDto(
    string Id,
    string Slug,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ApplicationSourceDto Source,
    ApplicationBuildDto Build,
    ApplicationRuntimeDto Runtime);

public sealed record ApplicationSourceDto(
    string GitRepoUrl,
    string Branch,
    string BaseDirectory,
    IReadOnlyList<string> WatchPaths,
    string? AccessTokenId);

public sealed record ApplicationBuildDto(
    string Type,
    string Path,
    IReadOnlyList<BuildArgDto> Args);

public sealed record BuildArgDto(string Key, string Value);

public sealed record ApplicationRuntimeDto(
    string TargetVmId,
    string ContainerName,
    IReadOnlyList<PortMappingDto> Ports,
    IReadOnlyList<VolumeMountDto> Volumes);

public sealed record PortMappingDto(int ContainerPort, int? HostPort, string Protocol);
public sealed record VolumeMountDto(string HostPath, string ContainerPath, bool ReadOnly);
