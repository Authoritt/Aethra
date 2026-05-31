using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.Events;

/// <summary>
/// Eventos de dominio del aggregate <c>Project</c>. Los aggregates hijos del modelo nuevo
/// (Template, Client, Instance) emiten sus propios eventos en sus respectivas carpetas.
/// </summary>
public sealed record ProjectCreatedEvent(ProjectId ProjectId, string Slug, string Name) : DomainEvent;

public sealed record ProjectRenamedEvent(ProjectId ProjectId, string OldName, string NewName) : DomainEvent;

/// <summary>
/// La metadata visual del proyecto (descripción, color, icono) cambió. No incluye el nombre —
/// para renombres se emite <see cref="ProjectRenamedEvent"/>.
/// </summary>
public sealed record ProjectAppearanceUpdatedEvent(
    ProjectId ProjectId,
    string? Description,
    string? Color,
    string? Icon) : DomainEvent;
