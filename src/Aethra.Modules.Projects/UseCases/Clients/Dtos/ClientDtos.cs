namespace Aethra.Modules.Projects.UseCases.Clients.Dtos;

/// <summary>
/// Vista de listado de un <c>Client</c>.
/// </summary>
public sealed record ClientSummary(
    string id,
    string projectId,
    string slug,
    string displayName,
    string? description,
    string? contactEmail,
    string? billingTag,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt);

/// <summary>
/// Detalle de un <c>Client</c>. Hoy idéntico a la summary; reservado para campos futuros
/// (contadores de instances, last activity, etc.) sin romper el contrato de la UI.
/// </summary>
public sealed record ClientDetail(
    string id,
    string projectId,
    string slug,
    string displayName,
    string? description,
    string? contactEmail,
    string? billingTag,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt);
