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

    private Project(ProjectId id, Slug slug, string name, DateTimeOffset now) : base(id)
    {
        Slug = slug;
        Name = name;
        CreatedAt = now;
        UpdatedAt = now;
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

    // EF Core
    private Project() : base() { Name = string.Empty; }
}
