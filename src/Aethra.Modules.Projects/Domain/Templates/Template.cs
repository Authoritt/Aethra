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
    /// Webhook secret cifrado con DataProtection (purpose <c>aethra-webhook-secrets</c>).
    /// El secret HMAC compartido con GitHub se cifra en reposo y sólo se descifra en memoria
    /// al momento de validar la firma de un payload entrante. Generado al crear; se puede rotar
    /// con <see cref="RotateWebhookSecret"/>.
    /// </summary>
    public byte[] WebhookSecretCipher { get; private set; }

    private readonly List<TemplateEnvironmentMapping> _environmentMapping = [];
    /// <summary>
    /// F12.3 — mapping <c>Environment → Branch</c> heredado por las <c>Instance</c>s que no
    /// definen <c>TrackedRef</c> explícito. Ver <see cref="TemplateEnvironmentMapping"/>.
    /// </summary>
    public IReadOnlyList<TemplateEnvironmentMapping> EnvironmentMapping => _environmentMapping.AsReadOnly();

    /// <summary>
    /// F12.3 — Cuando <c>true</c>, el webhook handler crea automáticamente Instances ephemerals
    /// para cada <c>pull_request.opened</c> con redeploys en <c>synchronize</c> y limpieza en
    /// <c>closed</c>. Default <c>false</c> — el operador habilita explícitamente.
    /// </summary>
    public bool AutoPreviewPullRequests { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Template(
        TemplateId id,
        ProjectId projectId,
        Slug slug,
        string name,
        TemplateSource source,
        TemplateBuild build,
        byte[] webhookSecretCipher,
        DateTimeOffset now) : base(id)
    {
        ProjectId = projectId;
        Slug = slug;
        Name = name;
        Source = source;
        Build = build;
        WebhookSecretCipher = webhookSecretCipher;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Crea un Template. El <paramref name="webhookSecretPlain"/> se cifra inmediatamente con
    /// <paramref name="codec"/>; el plain solo vive en memoria durante la transacción.
    /// </summary>
    public static Template Create(
        ProjectId projectId,
        Slug slug,
        string name,
        TemplateSource source,
        TemplateBuild build,
        string webhookSecretPlain,
        IWebhookSecretCodec codec,
        DateTimeOffset now,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del template no puede estar vacío.", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(webhookSecretPlain))
        {
            throw new ArgumentException("El webhookSecretPlain no puede estar vacío.", nameof(webhookSecretPlain));
        }

        var cipher = codec.Encode(webhookSecretPlain.Trim());
        var template = new Template(TemplateId.New(), projectId, slug, name.Trim(), source, build, cipher, now)
        {
            Description = description?.Trim(),
        };
        template.Raise(new TemplateCreatedEvent(
            template.Id,
            projectId,
            slug.Value,
            source.GitRepoUrl.Value,
            source.DefaultBranch));
        return template;
    }

    /// <summary>
    /// Reemplaza el origen Git. Útil cuando se cambia de monorepo a polirepo, o de branch.
    /// </summary>
    public void UpdateSource(TemplateSource source, DateTimeOffset now)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        UpdatedAt = now;
        Raise(new TemplateSourceUpdatedEvent(Id, source.GitRepoUrl.Value, source.DefaultBranch));
    }

    /// <summary>
    /// F12.3 — Reemplaza el set completo de <see cref="EnvironmentMapping"/>. Idempotente: si los
    /// items son iguales, no marca el aggregate como modificado.
    /// </summary>
    public void ReplaceEnvironmentMapping(IEnumerable<TemplateEnvironmentMapping> mappings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        // Normalizar y deduplicar por Environment (último gana).
        var dedup = new Dictionary<string, TemplateEnvironmentMapping>(StringComparer.Ordinal);
        foreach (var m in mappings)
        {
            dedup[m.Environment] = m;
        }
        _environmentMapping.Clear();
        _environmentMapping.AddRange(dedup.Values);
        UpdatedAt = now;
    }

    /// <summary>
    /// F12.3 — Habilita o deshabilita el auto-preview de PRs. Idempotente.
    /// </summary>
    public void SetAutoPreviewPullRequests(bool enabled, DateTimeOffset now)
    {
        if (AutoPreviewPullRequests == enabled)
        {
            return;
        }
        AutoPreviewPullRequests = enabled;
        UpdatedAt = now;
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
    /// Rota el secret usando el <paramref name="codec"/> para cifrar el nuevo valor.
    /// Devuelve el nuevo secret en plain para que el caller pueda mostrárselo al operador
    /// una sola vez. El anterior queda inválido inmediatamente.
    /// </summary>
    public string RotateWebhookSecret(IWebhookSecretCodec codec, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(codec);
        var newPlain = GenerateWebhookSecret();
        WebhookSecretCipher = codec.Encode(newPlain);
        UpdatedAt = now;
        Raise(new TemplateWebhookRotatedEvent(Id));
        return newPlain;
    }

    /// <summary>
    /// Genera un nuevo webhook secret en plaintext (no lo persiste).
    /// Útil para handlers que necesitan presentárselo al usuario antes de cifrarlo.
    /// </summary>
    public static string GenerateWebhookSecret()
        => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    // EF Core
    private Template() : base()
    {
        Name = string.Empty;
        WebhookSecretCipher = [];
        Source = default!;
        Build = default!;
    }
}
