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
    DateTimeOffset updatedAt);

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
