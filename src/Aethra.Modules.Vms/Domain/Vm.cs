using Aethra.Modules.Vms.Domain.Events;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Vms.Domain;

/// <summary>
/// VM gestionada por Aethra. Un satélite (1:1) la conecta al central vía SignalR.
/// </summary>
public sealed class Vm : AggregateRoot<VmId>
{
    /// <summary>Máximo de líneas conservadas en <see cref="InstallLog"/> (rolling buffer).</summary>
    public const int MaxInstallLogLines = 200;

    public Slug Slug { get; private set; }
    public string Name { get; private set; }
    public string? PublicIp { get; private set; }
    public string? PrivateIp { get; private set; }
    public string? Description { get; private set; }
    public VmStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastConnectedAt { get; private set; }
    public DateTimeOffset? LastDisconnectedAt { get; private set; }

    public Satellite Satellite { get; private set; }

    // Snapshot del satélite tras el último handshake (info estática del host).
    public string? Hostname { get; private set; }
    public string? KernelVersion { get; private set; }
    public string? CpuModel { get; private set; }
    public int? CpuCores { get; private set; }
    public long? TotalMemoryBytes { get; private set; }
    public string? ContainerRuntime { get; private set; }
    public string? ContainerRuntimeVersion { get; private set; }
    public long? RootDiskTotalBytes { get; private set; }
    public long? RootDiskAvailableBytes { get; private set; }
    public bool? RuntimeSocketAccessible { get; private set; }
    public string? DataVolumePath { get; private set; }
    public long? DataVolumeTotalBytes { get; private set; }
    public long? DataVolumeAvailableBytes { get; private set; }

    // F11.4 — Auto-instalación del satélite vía SSH.
    /// <summary>
    /// Credenciales SSH cifradas (DataProtection purpose <c>aethra-vm-ssh-creds</c>) para
    /// re-instalar / reparar el satélite sin pedirle al usuario que las vuelva a tipear.
    /// El plaintext es un JSON con <c>{ host, port, user, authMethod, value }</c>.
    /// </summary>
    public byte[]? SshCredentialsCipher { get; private set; }
    /// <summary>Estado de instalación del satélite. Default <see cref="InstallStatus.NotInstalled"/>.</summary>
    public InstallStatus InstallStatus { get; private set; }
    /// <summary>Último heartbeat del satélite (handshake o métricas). Lo usa el provisioner para verificar éxito.</summary>
    public DateTimeOffset? LastSeenAt { get; private set; }
    /// <summary>Log append-only del último intento de instalación. Rolling buffer de <see cref="MaxInstallLogLines"/> líneas.</summary>
    public string InstallLog { get; private set; }

    /// <summary>
    /// F12.3 — Cuando <c>true</c>, esta VM forma parte del pool round-robin de previews. Si el
    /// operador la apaga, las nuevas Instances ephemerals no se le asignan (las existentes no se
    /// mueven — solo el siguiente PR busca otra VM).
    /// </summary>
    public bool AcceptsPreviews { get; private set; }

    private Vm(VmId id, Slug slug, string name, Satellite satellite, DateTimeOffset now) : base(id)
    {
        Slug = slug;
        Name = name;
        Satellite = satellite;
        Status = VmStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        InstallStatus = InstallStatus.NotInstalled;
        InstallLog = string.Empty;
        AcceptsPreviews = true;
    }

    /// <summary>
    /// Registra una nueva VM y emite un token de satélite inicial.
    /// Devuelve también el plaintext del token (mostrar UNA SOLA VEZ al usuario).
    /// </summary>
    public static (string TokenPlaintext, Vm Vm) Register(Slug slug, string name, DateTimeOffset now,
        string? publicIp = null, string? privateIp = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre de la VM no puede estar vacío.", nameof(name));
        }
        var (plaintext, token) = SatelliteToken.Issue(now);
        var satellite = new Satellite(SatelliteId.New(), token);
        var vm = new Vm(VmId.New(), slug, name.Trim(), satellite, now)
        {
            PublicIp = publicIp?.Trim(),
            PrivateIp = privateIp?.Trim(),
            Description = description?.Trim(),
        };
        vm.Raise(new VmRegisteredEvent(vm.Id, vm.Name, vm.Slug.Value));
        return (plaintext, vm);
    }

    public string RotateToken(DateTimeOffset now)
    {
        var (plaintext, token) = SatelliteToken.Issue(now);
        Satellite.ReplaceToken(token);
        UpdatedAt = now;
        Raise(new SatelliteTokenRotatedEvent(Id, Satellite.Id));
        return plaintext;
    }

    public void RecordConnected(string hostname, string kernelVersion, string cpuModel, int cpuCores,
        long totalMemoryBytes, string agentVersion, DateTimeOffset now, string? containerRuntime = null,
        string? containerRuntimeVersion = null, long? rootDiskTotalBytes = null, long? rootDiskAvailableBytes = null,
        bool? runtimeSocketAccessible = null, string? dataVolumePath = null,
        long? dataVolumeTotalBytes = null, long? dataVolumeAvailableBytes = null)
    {
        Hostname = hostname;
        KernelVersion = kernelVersion;
        CpuModel = cpuModel;
        CpuCores = cpuCores;
        TotalMemoryBytes = totalMemoryBytes;
        ContainerRuntime = string.IsNullOrWhiteSpace(containerRuntime) ? null : containerRuntime.Trim();
        ContainerRuntimeVersion = string.IsNullOrWhiteSpace(containerRuntimeVersion) ? null : containerRuntimeVersion.Trim();
        RootDiskTotalBytes = rootDiskTotalBytes;
        RootDiskAvailableBytes = rootDiskAvailableBytes;
        RuntimeSocketAccessible = runtimeSocketAccessible;
        DataVolumePath = string.IsNullOrWhiteSpace(dataVolumePath) ? null : dataVolumePath.Trim();
        DataVolumeTotalBytes = dataVolumeTotalBytes;
        DataVolumeAvailableBytes = dataVolumeAvailableBytes;
        Satellite.RecordHandshake(agentVersion, now);
        Status = VmStatus.Connected;
        LastConnectedAt = now;
        LastSeenAt = now;
        // Si el satélite hace handshake, la instalación se considera exitosa
        // (transición desde Installing → Installed para cerrar el flow del provisioner).
        if (InstallStatus is InstallStatus.Installing or InstallStatus.NotInstalled)
        {
            InstallStatus = InstallStatus.Installed;
        }
        UpdatedAt = now;
        Raise(new SatelliteConnectedDomainEvent(Id, Satellite.Id, hostname, kernelVersion, cpuModel, cpuCores,
            totalMemoryBytes, agentVersion));
    }

    public void RecordDisconnected(string? reason, DateTimeOffset now)
    {
        Status = VmStatus.Disconnected;
        LastDisconnectedAt = now;
        UpdatedAt = now;
        Raise(new SatelliteDisconnectedDomainEvent(Id, Satellite.Id, reason));
    }

    public void RecordHeartbeat(DateTimeOffset now)
    {
        LastSeenAt = now;
    }

    public void UpdateMetadata(string? name, string? publicIp, string? privateIp, string? description,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }
        PublicIp = publicIp?.Trim();
        PrivateIp = privateIp?.Trim();
        Description = description?.Trim();
        UpdatedAt = now;
    }

    /// <summary>Persiste credenciales SSH cifradas para futuros reinstalls.</summary>
    public void SetSshCredentials(byte[]? cipher, DateTimeOffset now)
    {
        SshCredentialsCipher = cipher;
        UpdatedAt = now;
    }

    /// <summary>Cambia el <see cref="InstallStatus"/> y deja constancia en el log.</summary>
    public void BeginInstall(DateTimeOffset now)
    {
        InstallStatus = InstallStatus.Installing;
        UpdatedAt = now;
        ClearInstallLog();
        AppendInstallLog($"[{now:HH:mm:ss}] install_started");
    }

    public void MarkInstalled(DateTimeOffset now)
    {
        InstallStatus = InstallStatus.Installed;
        UpdatedAt = now;
        AppendInstallLog($"[{now:HH:mm:ss}] install_completed");
    }

    public void MarkInstallFailed(string errorCode, string errorMessage, DateTimeOffset now)
    {
        InstallStatus = InstallStatus.Failed;
        UpdatedAt = now;
        AppendInstallLog($"[{now:HH:mm:ss}] install_failed code={errorCode} message={errorMessage}");
    }

    /// <summary>
    /// Append una línea al rolling buffer de log. Si pasamos del máximo,
    /// dropeamos las más viejas para conservar tamaño acotado.
    /// </summary>
    public void AppendInstallLog(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }
        // Normaliza saltos y limita el largo individual de cada línea (8KB es razonable).
        var safe = line.Replace("\r\n", "\n").Replace("\r", "\n").Trim('\n');
        if (safe.Length > 8 * 1024)
        {
            safe = safe[..(8 * 1024)] + "…[truncated]";
        }
        var combined = string.IsNullOrEmpty(InstallLog) ? safe : InstallLog + "\n" + safe;
        var lines = combined.Split('\n');
        if (lines.Length > MaxInstallLogLines)
        {
            lines = lines[(lines.Length - MaxInstallLogLines)..];
        }
        InstallLog = string.Join('\n', lines);
    }

    public void ClearInstallLog() => InstallLog = string.Empty;

    /// <summary>
    /// F12.3 — Marca esta VM como elegible o no para hospedar previews ephemerals.
    /// Idempotente: si el estado ya es el deseado, no toca <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetAcceptsPreviews(bool accepts, DateTimeOffset now)
    {
        if (AcceptsPreviews == accepts)
        {
            return;
        }
        AcceptsPreviews = accepts;
        UpdatedAt = now;
    }

    // EF Core
    private Vm() : base()
    {
        Name = string.Empty;
        Satellite = default!;
        InstallLog = string.Empty;
        AcceptsPreviews = true;
    }
}
