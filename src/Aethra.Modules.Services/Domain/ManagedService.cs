using Aethra.Modules.Services.Domain.Events;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Services.Domain;

/// <summary>
/// Una instancia de servicio gestionado (postgres-main, redis-main, rabbit-main).
/// Vive como contenedor Docker en una VM target. Tiene su propio ciclo de vida y
/// puede tener N bindings (Applications que lo consumen).
///
/// Credenciales admin se persisten cifradas con DataProtection (purpose: "aethra-svc-admin").
/// </summary>
public sealed class ManagedService : AggregateRoot<ManagedServiceId>
{
    public string Slug { get; private set; }
    public string Name { get; private set; }
    public ServiceType Type { get; private set; }
    public string Version { get; private set; }
    public string TargetVmId { get; private set; }
    public string ContainerName { get; private set; }
    public string Image { get; private set; }
    public int InternalPort { get; private set; }
    public string NetworkName { get; private set; }
    public byte[] AdminCredentialsCipher { get; private set; }
    public bool ExposedExternally { get; private set; }
    public ManagedServiceStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ProvisionedAt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public BackupPolicy? BackupPolicy { get; private set; }
    public DateTimeOffset? LastBackupAt { get; private set; }
    public DateTimeOffset? LastRestoredAt { get; private set; }

    private ManagedService(ManagedServiceId id, string slug, string name, ServiceType type, string version,
        string targetVmId, string containerName, string image, int internalPort, string networkName,
        byte[] adminCredentialsCipher, bool exposedExternally, DateTimeOffset now) : base(id)
    {
        Slug = slug;
        Name = name;
        Type = type;
        Version = version;
        TargetVmId = targetVmId;
        ContainerName = containerName;
        Image = image;
        InternalPort = internalPort;
        NetworkName = networkName;
        AdminCredentialsCipher = adminCredentialsCipher;
        ExposedExternally = exposedExternally;
        Status = ManagedServiceStatus.Provisioning;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static ManagedService Create(string slug, string name, ServiceType type, string version,
        string targetVmId, string image, byte[] adminCredentialsCipher, DateTimeOffset now,
        int? internalPortOverride = null, bool exposedExternally = false, string? networkName = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Slug requerido.", nameof(slug));
        }
        if (string.IsNullOrWhiteSpace(targetVmId))
        {
            throw new ArgumentException("TargetVmId requerido.", nameof(targetVmId));
        }
        if (adminCredentialsCipher is null || adminCredentialsCipher.Length == 0)
        {
            throw new ArgumentException("AdminCredentials cifradas requeridas.", nameof(adminCredentialsCipher));
        }

        var port = internalPortOverride ?? type.DefaultInternalPort();
        var network = networkName ?? $"aethra_shared_{targetVmId.ToLowerInvariant()}";
        var containerName = slug.Trim().ToLowerInvariant();

        var svc = new ManagedService(ManagedServiceId.New(), slug.Trim().ToLowerInvariant(), name.Trim(),
            type, version.Trim(), targetVmId, containerName, image.Trim(), port, network,
            adminCredentialsCipher, exposedExternally, now);
        svc.Raise(new ManagedServiceCreatedEvent(svc.Id, type, svc.Slug, targetVmId));
        return svc;
    }

    /// <summary>
    /// Registra ("adopta") un servicio que YA existe como contenedor (creado fuera de Aethra) sin
    /// provisionarlo: arranca en <see cref="ManagedServiceStatus.Ready"/> con el ContainerName real
    /// y NO emite <c>ManagedServiceCreatedEvent</c> (no dispara al provisioner ni recrea nada).
    /// Da visibilidad en /services + /data-services a Postgres/Redis existentes sin tocar sus datos.
    /// </summary>
    public static ManagedService Adopt(string slug, string name, ServiceType type, string version,
        string targetVmId, string containerName, string image, int internalPort, string networkName,
        byte[] adminCredentialsCipher, DateTimeOffset now, bool exposedExternally = false)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Slug requerido.", nameof(slug));
        }
        if (string.IsNullOrWhiteSpace(targetVmId))
        {
            throw new ArgumentException("TargetVmId requerido.", nameof(targetVmId));
        }
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new ArgumentException("ContainerName requerido.", nameof(containerName));
        }
        if (adminCredentialsCipher is null || adminCredentialsCipher.Length == 0)
        {
            throw new ArgumentException("AdminCredentials cifradas requeridas.", nameof(adminCredentialsCipher));
        }

        var svc = new ManagedService(ManagedServiceId.New(), slug.Trim().ToLowerInvariant(), name.Trim(),
            type, version.Trim(), targetVmId, containerName.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(image) ? "(external)" : image.Trim(),
            internalPort, string.IsNullOrWhiteSpace(networkName) ? "aethra-net" : networkName.Trim(),
            adminCredentialsCipher, exposedExternally, now);
        // El contenedor ya existe y corre: queda Ready, sin evento de provisioning.
        svc.Status = ManagedServiceStatus.Ready;
        svc.ProvisionedAt = now;
        return svc;
    }

    public void MarkProvisioned(DateTimeOffset now)
    {
        if (Status is ManagedServiceStatus.Ready) { return; }
        Status = ManagedServiceStatus.Ready;
        ProvisionedAt = now;
        UpdatedAt = now;
        ErrorCode = null;
        ErrorMessage = null;
        Raise(new ManagedServiceProvisionedEvent(Id, Slug));
    }

    public void MarkFailed(string code, string message, DateTimeOffset now)
    {
        Status = ManagedServiceStatus.Failed;
        ErrorCode = code;
        ErrorMessage = message;
        UpdatedAt = now;
        Raise(new ManagedServiceFailedEvent(Id, Slug, code, message));
    }

    public void MarkStopped(DateTimeOffset now)
    {
        Status = ManagedServiceStatus.Stopped;
        UpdatedAt = now;
    }

    /// <summary>
    /// Actualiza metadata editable del servicio: nombre display y si se expone externamente.
    /// El <see cref="Slug"/>, <see cref="Image"/>, <see cref="InternalPort"/> y <see cref="TargetVmId"/>
    /// NO se pueden cambiar (rompería el contenedor y los bindings ya provisionados).
    /// </summary>
    public void UpdateInfo(string name, bool exposedExternally, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre no puede estar vacío.", nameof(name));
        }
        Name = name.Trim();
        ExposedExternally = exposedExternally;
        UpdatedAt = now;
    }

    public void UpdateAdminCredentials(byte[] cipher, DateTimeOffset now)
    {
        AdminCredentialsCipher = cipher;
        UpdatedAt = now;
    }

    public void SetBackupPolicy(BackupPolicy? policy, DateTimeOffset now)
    {
        if (policy is not null && !policy.IsValid())
        {
            throw new ArgumentException("BackupPolicy invalida (cron/retention/destination).", nameof(policy));
        }
        BackupPolicy = policy;
        UpdatedAt = now;
    }

    public void MarkBackupCompleted(DateTimeOffset now)
    {
        LastBackupAt = now;
        UpdatedAt = now;
    }

    public void MarkRestored(DateTimeOffset now)
    {
        LastRestoredAt = now;
        UpdatedAt = now;
    }

    // EF Core
    private ManagedService() : base()
    {
        Slug = string.Empty;
        Name = string.Empty;
        Version = string.Empty;
        TargetVmId = string.Empty;
        ContainerName = string.Empty;
        Image = string.Empty;
        NetworkName = string.Empty;
        AdminCredentialsCipher = [];
    }
}
