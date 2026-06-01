namespace Aethra.Modules.Projects.UseCases.Projects.Dtos;

/// <summary>
/// Vista de listado: campos administrativos + contadores derivados (templates/clients) que la
/// UI usa para badges sin tener que hacer una segunda query por proyecto.
/// </summary>
public sealed record ProjectSummary(
    string id,
    string slug,
    string name,
    string? description,
    string? color,
    string? icon,
    int templateCount,
    int clientCount,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt);

/// <summary>
/// Vista de detalle: mismos campos que <see cref="ProjectSummary"/>; el endpoint específico de
/// Templates/Clients del proyecto se consulta aparte para no inflar la respuesta del detalle.
/// </summary>
public sealed record ProjectDetail(
    string id,
    string slug,
    string name,
    string? description,
    string? color,
    string? icon,
    int templateCount,
    int clientCount,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt);
