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

    public void UpdateAdminCredentials(byte[] cipher, DateTimeOffset now)
    {
        AdminCredentialsCipher = cipher;
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
