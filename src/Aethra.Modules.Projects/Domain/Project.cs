using Aethra.Modules.Projects.Domain.Events;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Projects.Domain;

/// <summary>
/// Agrupación lógica. Un Project puede tener N <see cref="Environment"/>s; cada uno con N
/// <see cref="Application"/>s. El Slug es único globalmente.
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

    private readonly List<Environment> _environments = [];
    public IReadOnlyList<Environment> Environments => _environments.AsReadOnly();

    private Project(ProjectId id, Slug slug, string name, DateTimeOffset now) : base(id)
    {
        Slug = slug;
        Name = name;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Project Create(Slug slug, string name, DateTimeOffset now, string? description = null,
        string? color = null, string? icon = null, string defaultEnvironmentName = "production")
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
        project.AddEnvironment(defaultEnvironmentName, now);
        project.Raise(new ProjectCreatedEvent(project.Id, slug.Value, project.Name));
        return project;
    }

    public Environment AddEnvironment(string name, DateTimeOffset now)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (_environments.Any(e => e.Name == normalized))
        {
            throw new InvalidOperationException($"El environment '{normalized}' ya existe en el proyecto.");
        }
        var env = Environment.Create(Id, normalized, now);
        _environments.Add(env);
        UpdatedAt = now;
        Raise(new EnvironmentAddedEvent(Id, env.Id, env.Name));
        return env;
    }

    public bool RemoveEnvironment(EnvironmentId envId, DateTimeOffset now)
    {
        var env = _environments.FirstOrDefault(e => e.Id == envId);
        if (env is null)
        {
            return false;
        }
        if (env.Applications.Count > 0)
        {
            throw new InvalidOperationException(
                "No se puede eliminar un environment con applications. Elimine primero las apps.");
        }
        _environments.Remove(env);
        UpdatedAt = now;
        Raise(new EnvironmentRemovedEvent(Id, envId));
        return true;
    }

    public Application AddApplication(EnvironmentId envId, Application application, DateTimeOffset now)
    {
        var env = _environments.FirstOrDefault(e => e.Id == envId)
            ?? throw new InvalidOperationException($"Environment '{envId}' no existe en el proyecto.");
        env.AddApplication(application);
        UpdatedAt = now;
        return application;
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

    public void UpdateMetadata(string? description, string? color, string? icon, DateTimeOffset now)
    {
        Description = description?.Trim();
        Color = color?.Trim();
        Icon = icon?.Trim();
        UpdatedAt = now;
    }

    public void MarkDeleted()
    {
        Raise(new ProjectDeletedEvent(Id));
    }

    // EF Core
    private Project() : base() { Name = string.Empty; }
}
