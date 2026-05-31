using Aethra.Modules.Vms.Domain.Events;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Vms.Domain;

/// <summary>
/// VM gestionada por Aethra. Un satélite (1:1) la conecta al central vía SignalR.
/// </summary>
public sealed class Vm : AggregateRoot<VmId>
{
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

    private Vm(VmId id, Slug slug, string name, Satellite satellite, DateTimeOffset now) : base(id)
    {
        Slug = slug;
        Name = name;
        Satellite = satellite;
        Status = VmStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
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
        long totalMemoryBytes, string agentVersion, DateTimeOffset now)
    {
        Hostname = hostname;
        KernelVersion = kernelVersion;
        CpuModel = cpuModel;
        CpuCores = cpuCores;
        TotalMemoryBytes = totalMemoryBytes;
        Satellite.RecordHandshake(agentVersion, now);
        Status = VmStatus.Connected;
        LastConnectedAt = now;
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

    // EF Core
    private Vm() : base() { Name = string.Empty; Satellite = default!; }
}
