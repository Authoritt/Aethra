using Aethra.Modules.Projects.Domain.Templates.Events;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Projects.Domain.Templates;

/// <summary>
/// Recipe de despliegue: define <em>cómo</em> se construye una unidad de software (repo Git,
/// branch, estrategia de build). NO contiene tenants — eso vive en <c>Instance</c>.
///
/// Un Template existe dentro de un Project, con <c>Slug</c> único por Project.
/// Un mismo Template alimenta N Instances (cada una para un Client distinto).
/// </summary>
public sealed class Template : AggregateRoot<TemplateId>
{
    public ProjectId ProjectId { get; private set; }
    public Slug Slug { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    public TemplateSource Source { get; private set; }
    public TemplateBuild Build { get; private set; }

    /// <summary>
    /// Secret HMAC con el que se firman los webhooks entrantes para este template.
    /// Generado al crear; se puede rotar con <see cref="RotateWebhookSecret"/>.
    /// </summary>
    public string WebhookSecret { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Template(
        TemplateId id,
        ProjectId projectId,
        Slug slug,
        string name,
        TemplateSource source,
        TemplateBuild build,
        string webhookSecret,
        DateTimeOffset now) : base(id)
    {
        ProjectId = projectId;
        Slug = slug;
        Name = name;
        Source = source;
        Build = build;
        WebhookSecret = webhookSecret;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Template Create(
        ProjectId projectId,
        Slug slug,
        string name,
        TemplateSource source,
        TemplateBuild build,
        string? webhookSecret,
        DateTimeOffset now,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del template no puede estar vacío.", nameof(name));
        }

        var secret = string.IsNullOrWhiteSpace(webhookSecret) ? GenerateWebhookSecret() : webhookSecret.Trim();
        var template = new Template(TemplateId.New(), projectId, slug, name.Trim(), source, build, secret, now)
        {
            Description = description?.Trim(),
        };
        template.Raise(new TemplateCreatedEvent(
            template.Id,
            projectId,
            slug.Value,
            source.GitRepoUrl.Value,
            source.Branch));
        return template;
    }

    /// <summary>
    /// Reemplaza el origen Git. Útil cuando se cambia de monorepo a polirepo, o de branch.
    /// </summary>
    public void UpdateSource(TemplateSource source, DateTimeOffset now)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        UpdatedAt = now;
        Raise(new TemplateSourceUpdatedEvent(Id, source.GitRepoUrl.Value, source.Branch));
    }

    /// <summary>
    /// Reemplaza la estrategia de build (Dockerfile vs Compose vs Nixpacks, paths, args).
    /// No dispara evento — el siguiente deploy recogerá la nueva config.
    /// </summary>
    public void UpdateBuild(TemplateBuild build, DateTimeOffset now)
    {
        Build = build ?? throw new ArgumentNullException(nameof(build));
        UpdatedAt = now;
    }

    /// <summary>
    /// Renombra el template. El slug NO cambia (rompería webhooks ya configurados).
    /// </summary>
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
        Name = newName.Trim();
        UpdatedAt = now;
    }

    public void UpdateDescription(string? description, DateTimeOffset now)
    {
        Description = description?.Trim();
        UpdatedAt = now;
    }

    /// <summary>
    /// Genera un nuevo webhook secret. El anterior queda inválido inmediatamente.
    /// </summary>
    public void RotateWebhookSecret(DateTimeOffset now)
    {
        WebhookSecret = GenerateWebhookSecret();
        UpdatedAt = now;
        Raise(new TemplateWebhookRotatedEvent(Id));
    }

    private static string GenerateWebhookSecret()
        => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    // EF Core
    private Template() : base()
    {
        Name = string.Empty;
        WebhookSecret = string.Empty;
        Source = default!;
        Build = default!;
    }
}
