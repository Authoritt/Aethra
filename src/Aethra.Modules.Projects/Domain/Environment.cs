using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain;

/// <summary>
/// Etapa de despliegue dentro de un <see cref="Project"/>: "production", "staging", "dev".
/// Aísla configuración (env vars, dominios) por etapa.
/// </summary>
public sealed class Environment : Entity<EnvironmentId>
{
    public ProjectId ProjectId { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<Application> _applications = [];
    public IReadOnlyList<Application> Applications => _applications.AsReadOnly();

    private Environment(EnvironmentId id, ProjectId projectId, string name, DateTimeOffset now)
        : base(id)
    {
        ProjectId = projectId;
        Name = name;
        CreatedAt = now;
    }

    internal static Environment Create(ProjectId projectId, string name, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del environment no puede estar vacío.", nameof(name));
        }
        return new Environment(EnvironmentId.New(), projectId, name.Trim().ToLowerInvariant(), now);
    }

    internal void AddApplication(Application app)
    {
        if (_applications.Any(a => a.Slug == app.Slug))
        {
            throw new InvalidOperationException(
                $"Ya existe una Application con slug '{app.Slug}' en el environment '{Name}'.");
        }
        _applications.Add(app);
    }

    internal bool RemoveApplication(ApplicationId appId)
    {
        var index = _applications.FindIndex(a => a.Id == appId);
        if (index < 0)
        {
            return false;
        }
        _applications.RemoveAt(index);
        return true;
    }

    // EF Core
    private Environment() : base() { Name = string.Empty; }
}
