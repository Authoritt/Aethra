namespace Aethra.Modules.Projects.UseCases.Templates.Dtos;

/// <summary>
/// Vista de listado de un <c>Template</c>. No incluye webhook secret — solo se muestra al crear.
/// </summary>
public sealed record TemplateSummary(
    string id,
    string projectId,
    string slug,
    string name,
    string? description,
    string gitRepoUrl,
    string branch,
    string buildType,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt);

/// <summary>
/// Vista de detalle de un <c>Template</c>. La UI muestra el secret una sola vez (al crear) —
/// posteriores lecturas devuelven un placeholder por seguridad.
/// </summary>
public sealed record TemplateDetail(
    string id,
    string projectId,
    string slug,
    string name,
    string? description,
    string gitRepoUrl,
    string branch,
    string baseDirectory,
    IReadOnlyList<string> watchPaths,
    string? accessTokenCredentialName,
    string buildType,
    string dockerfilePath,
    string? composeFilePath,
    IReadOnlyList<TemplateBuildArgDto> buildArgs,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt,
    IReadOnlyList<TemplateEnvironmentMappingDto> environmentMapping,
    bool autoPreviewPullRequests,
    IReadOnlyList<TemplateServiceDto> services);

/// <summary>F12.3 — row de mapping Environment→Branch para la vista detalle.</summary>
public sealed record TemplateEnvironmentMappingDto(string environment, string branch);

/// <summary>F13 — servicio multi-contenedor del template para la vista detalle.</summary>
public sealed record TemplateServiceDto(
    string name,
    string image,
    int port,
    IReadOnlyList<string> pathPrefixes,
    IReadOnlyList<TemplateBuildArgDto> env,
    string buildMode,
    string? dockerfilePath,
    IReadOnlyList<TemplateServiceVolumeDto> volumes,
    string? hostname);

/// <summary>F13.3 — volumen persistente de un servicio para la vista detalle.</summary>
public sealed record TemplateServiceVolumeDto(
    string name,
    string containerPath,
    bool readOnly);

/// <summary>
/// Respuesta del POST create: incluye el <c>webhookSecret</c> en plain — única oportunidad
/// para que el operador lo copie a su CI.
/// </summary>
public sealed record TemplateCreatedResult(
    string id,
    string projectId,
    string slug,
    string name,
    string webhookSecret,
    DateTimeOffset createdAt);

public sealed record TemplateBuildArgDto(string key, string value);

/// <summary>
/// F11.2 — Resultado del POST <c>/api/templates/discover</c>. Indica qué estrategia de build
/// recomendamos para el repo inspeccionado y qué puertos exponer por defecto en el form.
/// </summary>
public sealed record TemplateDiscoverResult(
    IReadOnlyList<string> detectedLanguages,
    bool hasDockerfile,
    bool hasCompose,
    bool hasNixpacksToml,
    string suggestedBuildType,
    IReadOnlyList<int> exposedPorts);

