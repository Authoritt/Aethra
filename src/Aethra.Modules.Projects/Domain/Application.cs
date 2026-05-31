using Aethra.Modules.Projects.Domain.Events;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Projects.Domain;

/// <summary>
/// Lo desplegable. Cada Application pertenece a un <see cref="Environment"/> y tiene su
/// propio source Git, build, runtime y variables de entorno.
/// Múltiples Applications pueden compartir el mismo repo (monorepo) — se diferencian por
/// <see cref="ApplicationSource.BaseDirectory"/> y <see cref="ApplicationSource.WatchPaths"/>.
/// </summary>
public sealed class Application : AggregateRoot<ApplicationId>
{
    public EnvironmentId EnvironmentId { get; private set; }
    public Slug Slug { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public ApplicationSource Source { get; private set; }
    public ApplicationBuild Build { get; private set; }
    public ApplicationRuntime Runtime { get; private set; }

    private Application(
        ApplicationId id,
        EnvironmentId environmentId,
        Slug slug,
        string name,
        ApplicationSource source,
        ApplicationBuild build,
        ApplicationRuntime runtime,
        DateTimeOffset now) : base(id)
    {
        EnvironmentId = environmentId;
        Slug = slug;
        Name = name;
        Source = source;
        Build = build;
        Runtime = runtime;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Application Create(
        EnvironmentId environmentId,
        Slug slug,
        string name,
        ApplicationSource source,
        ApplicationBuild build,
        ApplicationRuntime runtime,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre no puede estar vacío.", nameof(name));
        }

        var app = new Application(ApplicationId.New(), environmentId, slug, name.Trim(), source, build, runtime, now);
        app.Raise(new ApplicationCreatedEvent(
            ProjectId: default,           // lo rellena el agregado Project al adjuntar
            EnvironmentId: environmentId,
            ApplicationId: app.Id,
            Slug: slug.Value,
            Name: app.Name,
            GitRepoUrl: source.GitRepoUrl.Value,
            Branch: source.Branch));
        return app;
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
        Raise(new ApplicationRenamedEvent(Id, old, Name));
    }

    public void ReplaceSource(ApplicationSource source, DateTimeOffset now)
    {
        Source = source;
        UpdatedAt = now;
    }

    public void ReplaceBuild(ApplicationBuild build, DateTimeOffset now)
    {
        Build = build;
        UpdatedAt = now;
    }

    public void ReplaceRuntime(ApplicationRuntime runtime, DateTimeOffset now)
    {
        Runtime = runtime;
        UpdatedAt = now;
    }

    // EF Core
    private Application() : base() { Name = string.Empty; Source = default!; Build = default!; Runtime = default!; }
}
