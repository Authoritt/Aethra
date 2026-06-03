using Aethra.Modules.Projects.Domain.Events;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Projects.Domain;

/// <summary>
/// Agrupación lógica de alto nivel. En el modelo F9 un Project contiene N
/// <c>Templates</c> (recipes de build), N <c>Clients</c> (tenants) y, derivadamente, N
/// <c>Instances</c> (Template × Client × Environment). Aquí solo persistimos la metadata
/// administrativa — las colecciones hijas viven en sus propios aggregates para evitar
/// loadear todo en cada query.
///
/// El <see cref="Slug"/> es único globalmente.
/// </summary>
public sealed class Project : AggregateRoot<ProjectId>
{
    public Slug Slug { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string? Color { get; private set; }
    public string? Icon { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// F12.3 — Tope de previews concurrentes (Instances <c>IsEphemeral=true</c>) activas en este
    /// Project. Default <c>10</c>. Cuando se alcanza, los webhooks de PR responden con un comment
    /// "quota exceeded" en lugar de crear más Instances.
    /// </summary>
    public int PreviewMaxConcurrent { get; private set; }

    /// <summary>
    /// F12.3 — FK lazy al <c>Client</c> interno <c>__preview__</c> usado como tenant de todas las
    /// Instances ephemerals del Project. Se crea on-demand en el webhook handler al primer PR.
    /// <c>null</c> hasta entonces.
    /// </summary>
    public string? PreviewClientId { get; private set; }

    private Project(ProjectId id, Slug slug, string name, DateTimeOffset now) : base(id)
    {
        Slug = slug;
        Name = name;
        CreatedAt = now;
        UpdatedAt = now;
        PreviewMaxConcurrent = 10;
    }

    public static Project Create(
        Slug slug,
        string name,
        DateTimeOffset now,
        string? description = null,
        string? color = null,
        string? icon = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del proyecto no puede estar vacío.", nameof(name));
        }
        var project = new Project(ProjectId.New(), slug, name.Trim(), now)
        {
            Description = description?.Trim(),
            Color = color?.Trim(),
            Icon = icon?.Trim(),
        };
        project.Raise(new ProjectCreatedEvent(project.Id, slug.Value, project.Name));
        return project;
    }

    public void Rename(string newName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("El nombre no puede estar vacío.", nameof(newName));
        }
        if (newName.Trim() == Name)
        {
            return;
        }
        var old = Name;
        Name = newName.Trim();
        UpdatedAt = now;
        Raise(new ProjectRenamedEvent(Id, old, Name));
    }

    /// <summary>
    /// Actualiza descripción, color e icono. Cualquiera puede ser <c>null</c> para limpiar.
    /// Emite <see cref="ProjectAppearanceUpdatedEvent"/> solo si efectivamente cambió algo.
    /// </summary>
    public void UpdateAppearance(string? description, string? color, string? icon, DateTimeOffset now)
    {
        var newDescription = description?.Trim();
        var newColor = color?.Trim();
        var newIcon = icon?.Trim();
        if (newDescription == Description && newColor == Color && newIcon == Icon)
        {
            return;
        }
        Description = newDescription;
        Color = newColor;
        Icon = newIcon;
        UpdatedAt = now;
        Raise(new ProjectAppearanceUpdatedEvent(Id, Description, Color, Icon));
    }

    /// <summary>
    /// F12.3 — Setea el tope de previews concurrentes. Valor mínimo 0 (deshabilita previews).
    /// </summary>
    public void SetPreviewMaxConcurrent(int newMax, DateTimeOffset now)
    {
        if (newMax < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newMax), "PreviewMaxConcurrent no puede ser negativo.");
        }
        if (PreviewMaxConcurrent == newMax)
        {
            return;
        }
        PreviewMaxConcurrent = newMax;
        UpdatedAt = now;
    }

    /// <summary>
    /// F12.3 — Setea el FK al Client interno <c>__preview__</c>. Sólo se llama una vez (lazy create
    /// desde el webhook handler). Idempotente: si ya estaba seteado, no toca <see cref="UpdatedAt"/>.
    /// </summary>
    public void AttachPreviewClient(string clientId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("clientId requerido.", nameof(clientId));
        }
        var normalized = clientId.Trim();
        if (PreviewClientId == normalized)
        {
            return;
        }
        PreviewClientId = normalized;
        UpdatedAt = now;
    }

    // EF Core
    private Project() : base()
    {
        Name = string.Empty;
        PreviewMaxConcurrent = 10;
    }
}
