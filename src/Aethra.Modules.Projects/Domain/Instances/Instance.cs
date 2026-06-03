using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Instances.Events;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Projects.Domain.Instances;

/// <summary>
/// Despliegue concreto de un <see cref="Template"/> para un <see cref="Client"/> en un
/// environment dado. Es la unidad mínima que el módulo Deployments construye y ejecuta.
///
/// Pertenece a un Template (FK <see cref="TemplateId"/>) y referencia un Client
/// (<see cref="ClientId"/>). Heredan el Project a través del Template — no se persiste FK directo.
///
/// El <see cref="ContainerName"/> y los nombres de volumen se derivan de
/// <c>{template.slug}-{client.slug}-{environment}</c> para asegurar nombres únicos y trazables
/// entre VMs sin colisiones cross-tenant.
/// </summary>
public sealed class Instance : AggregateRoot<InstanceId>
{
    public TemplateId TemplateId { get; private set; }
    public ClientId ClientId { get; private set; }

    /// <summary>
    /// Environment lógico ("production", "staging", "dev"). Validado contra
    /// <c>Settings.Environments</c> en el handler — el aggregate no conoce la whitelist global.
    /// Se almacena lowercased.
    /// </summary>
    public string Environment { get; private set; }

    /// <summary>
    /// Slug único por Template. Por defecto se autogenera como
    /// <c>{client.slug}-{environment}</c> en la fábrica; el caller puede pasar override.
    /// </summary>
    public string Slug { get; private set; }

    public string TargetVmId { get; private set; }

    /// <summary>
    /// Nombre del contenedor Docker. Se compone automáticamente como
    /// <c>{template.slug}-{client.slug}-{environment}</c> (no se acepta override desde fuera —
    /// requiere coherencia con health/log queries).
    /// </summary>
    public string ContainerName { get; private set; }

    private readonly List<PortMapping> _ports = [];
    public IReadOnlyList<PortMapping> Ports => _ports.AsReadOnly();

    private readonly List<VolumeMount> _volumes = [];
    public IReadOnlyList<VolumeMount> Volumes => _volumes.AsReadOnly();

    public Healthcheck? Healthcheck { get; private set; }

    public bool AutoDeployOnNewBuild { get; private set; }

    /// <summary>
    /// FQDN custom (override del auto-hostname). <c>null</c> ⇒ se usa <see cref="AutoHostname"/>.
    /// </summary>
    public string? CustomDomain { get; private set; }

    /// <summary>
    /// Hostname auto-calculado a partir de <c>Settings.BaseDomain</c>. Se setea desde el handler
    /// (no en la fábrica) porque el aggregate no debe leer Settings — se almacena para
    /// consistency entre lecturas y para que YARP no recalcule en cada request.
    /// </summary>
    public string? AutoHostname { get; private set; }

    /// <summary>
    /// F12.3 — Git ref que esta Instance trackea explícitamente. <c>null</c> ⇒ usar la cascada
    /// <see cref="Template.EnvironmentMapping"/> → <c>Template.Source.DefaultBranch</c> resuelta en
    /// <see cref="ResolveTrackedRef"/>. Ejemplos: <c>refs/heads/develop</c>, <c>refs/pull/42/head</c>.
    /// </summary>
    public string? TrackedRef { get; private set; }

    /// <summary>
    /// F12.3 — <c>true</c> si la Instance fue creada automáticamente para previsualizar un Pull
    /// Request. Se borra cuando el PR cierra. No se permite mutar manualmente desde la UI.
    /// </summary>
    public bool IsEphemeral { get; private set; }

    /// <summary>
    /// F12.3 — Si está seteado, la Instance debe ser eliminada por el cleanup service tras esta
    /// fecha (safety net para previews que nunca recibieron el webhook <c>closed</c>).
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>
    /// F12.3 — UserId del operador que creó la Instance. Permite filtrar "Mis previews" en la UI y
    /// notificar al autor del PR cuando se rompe el deploy de preview. <c>null</c> en datos legacy.
    /// </summary>
    public string? CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Instance(
        InstanceId id,
        TemplateId templateId,
        ClientId clientId,
        string environment,
        string slug,
        string targetVmId,
        string containerName,
        bool autoDeployOnNewBuild,
        DateTimeOffset now) : base(id)
    {
        TemplateId = templateId;
        ClientId = clientId;
        Environment = environment;
        Slug = slug;
        TargetVmId = targetVmId;
        ContainerName = containerName;
        AutoDeployOnNewBuild = autoDeployOnNewBuild;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Instance Create(
        TemplateId templateId,
        ClientId clientId,
        string environment,
        string targetVmId,
        string templateSlug,
        string clientSlug,
        IReadOnlyList<PortMapping>? ports,
        IReadOnlyList<VolumeMount>? volumes,
        Healthcheck? healthcheck,
        bool autoDeployOnNewBuild,
        DateTimeOffset now,
        string? slugOverride = null,
        string? trackedRef = null,
        bool isEphemeral = false,
        DateTimeOffset? expiresAt = null,
        string? createdByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(environment))
        {
            throw new ArgumentException("Environment requerido.", nameof(environment));
        }
        if (string.IsNullOrWhiteSpace(targetVmId))
        {
            throw new ArgumentException("TargetVmId requerido.", nameof(targetVmId));
        }
        if (string.IsNullOrWhiteSpace(templateSlug))
        {
            throw new ArgumentException("TemplateSlug requerido para componer ContainerName.", nameof(templateSlug));
        }
        if (string.IsNullOrWhiteSpace(clientSlug))
        {
            throw new ArgumentException("ClientSlug requerido para componer ContainerName.", nameof(clientSlug));
        }

        var env = environment.Trim().ToLowerInvariant();
        var tslug = templateSlug.Trim().ToLowerInvariant();
        var cslug = clientSlug.Trim().ToLowerInvariant();
        var slug = string.IsNullOrWhiteSpace(slugOverride)
            ? $"{cslug}-{env}"
            : slugOverride.Trim().ToLowerInvariant();
        var containerName = $"{tslug}-{cslug}-{env}";

        var instance = new Instance(
            InstanceId.New(),
            templateId,
            clientId,
            env,
            slug,
            targetVmId.Trim(),
            containerName,
            autoDeployOnNewBuild,
            now);

        if (ports is { Count: > 0 })
        {
            instance._ports.AddRange(ports);
        }
        if (volumes is { Count: > 0 })
        {
            instance._volumes.AddRange(PrefixVolumes(volumes, tslug, cslug));
        }
        instance.Healthcheck = healthcheck;
        instance.TrackedRef = string.IsNullOrWhiteSpace(trackedRef) ? null : trackedRef.Trim();
        instance.IsEphemeral = isEphemeral;
        instance.ExpiresAt = expiresAt;
        instance.CreatedByUserId = string.IsNullOrWhiteSpace(createdByUserId) ? null : createdByUserId.Trim();

        instance.Raise(new InstanceCreatedEvent(
            instance.Id,
            templateId,
            clientId,
            env,
            instance.TargetVmId,
            containerName));
        return instance;
    }

    /// <summary>
    /// F12.3 — Resuelve el git ref efectivo de la Instance, aplicando la cascada:
    /// <c>TrackedRef</c> propio (si seteado) → mapping del Template para este <c>Environment</c> →
    /// <c>Template.Source.DefaultBranch</c>. Siempre devuelve un ref Git válido (formato
    /// <c>refs/heads/...</c> para branches normales o <c>refs/pull/N/head</c> para previews).
    /// </summary>
    public string ResolveTrackedRef(Template template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (!string.IsNullOrWhiteSpace(TrackedRef))
        {
            return TrackedRef;
        }
        var fromMapping = template.EnvironmentMapping.FirstOrDefault(m => m.Environment == Environment);
        if (fromMapping is not null)
        {
            return $"refs/heads/{fromMapping.Branch}";
        }
        return $"refs/heads/{template.Source.DefaultBranch}";
    }

    /// <summary>
    /// F12.3 — Setea o limpia el <see cref="TrackedRef"/>. Pasar <c>null</c>/whitespace para volver
    /// a la cascada de resolución del Template.
    /// </summary>
    public void SetTrackedRef(string? trackedRef, DateTimeOffset now)
    {
        var normalized = string.IsNullOrWhiteSpace(trackedRef) ? null : trackedRef.Trim();
        if (normalized == TrackedRef)
        {
            return;
        }
        TrackedRef = normalized;
        UpdatedAt = now;
    }

    /// <summary>
    /// Reemplaza la configuración runtime (VM, puertos, volúmenes, healthcheck) en una sola
    /// operación atómica para no dejar la Instance en estado parcial entre eventos.
    /// </summary>
    public void UpdateRuntime(
        string targetVmId,
        IReadOnlyList<PortMapping> ports,
        IReadOnlyList<VolumeMount> volumes,
        Healthcheck? healthcheck,
        string templateSlug,
        string clientSlug,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(targetVmId))
        {
            throw new ArgumentException("TargetVmId requerido.", nameof(targetVmId));
        }
        TargetVmId = targetVmId.Trim();
        _ports.Clear();
        _ports.AddRange(ports ?? []);
        _volumes.Clear();
        _volumes.AddRange(PrefixVolumes(volumes ?? [], templateSlug, clientSlug));
        Healthcheck = healthcheck;
        UpdatedAt = now;
        Raise(new InstanceRuntimeUpdatedEvent(Id, TargetVmId));
    }

    public void EnableAutoDeploy(DateTimeOffset now)
    {
        if (AutoDeployOnNewBuild)
        {
            return;
        }
        AutoDeployOnNewBuild = true;
        UpdatedAt = now;
        Raise(new InstanceAutoDeployChangedEvent(Id, true));
    }

    public void DisableAutoDeploy(DateTimeOffset now)
    {
        if (!AutoDeployOnNewBuild)
        {
            return;
        }
        AutoDeployOnNewBuild = false;
        UpdatedAt = now;
        Raise(new InstanceAutoDeployChangedEvent(Id, false));
    }

    /// <summary>
    /// Setea o limpia un dominio custom. <c>null</c>/whitespace ⇒ vuelve al auto-hostname.
    /// La validación de FQDN se delega al handler/value-object aguas arriba; aquí solo se
    /// almacena trimmed.
    /// </summary>
    public void SetCustomDomain(string? customDomain, DateTimeOffset now)
    {
        var normalized = string.IsNullOrWhiteSpace(customDomain) ? null : customDomain.Trim().ToLowerInvariant();
        if (normalized == CustomDomain)
        {
            return;
        }
        CustomDomain = normalized;
        UpdatedAt = now;
        Raise(new InstanceCustomDomainChangedEvent(Id, normalized));
    }

    /// <summary>
    /// Persiste el hostname auto-calculado. Llamado por el handler tras leer
    /// <c>Settings.BaseDomain</c>. No emite evento — el cambio es derivado.
    /// </summary>
    public void SetAutoHostname(string autoHostname, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(autoHostname))
        {
            throw new ArgumentException("AutoHostname no puede estar vacío.", nameof(autoHostname));
        }
        AutoHostname = autoHostname.Trim().ToLowerInvariant();
        UpdatedAt = now;
    }

    private static IEnumerable<VolumeMount> PrefixVolumes(
        IReadOnlyList<VolumeMount> volumes,
        string templateSlug,
        string clientSlug)
    {
        var prefix = $"{templateSlug}-{clientSlug}";
        foreach (var v in volumes)
        {
            if (v.Name.StartsWith(prefix + "-", StringComparison.Ordinal) || v.Name == prefix)
            {
                yield return v;
            }
            else
            {
                yield return v with { Name = $"{prefix}-{v.Name}" };
            }
        }
    }

    // EF Core
    private Instance() : base()
    {
        Environment = string.Empty;
        Slug = string.Empty;
        TargetVmId = string.Empty;
        ContainerName = string.Empty;
    }
}
