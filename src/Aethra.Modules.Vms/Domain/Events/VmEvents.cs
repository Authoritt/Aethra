using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Vms.Domain.Events;

public sealed record VmRegisteredEvent(VmId VmId, string Name, string Slug) : DomainEvent;

public sealed record SatelliteTokenRotatedEvent(VmId VmId, SatelliteId SatelliteId) : DomainEvent;

public sealed record SatelliteConnectedDomainEvent(
    VmId VmId,
    SatelliteId SatelliteId,
    string Hostname,
    string KernelVersion,
    string CpuModel,
    int CpuCores,
    long TotalMemoryBytes,
    string AgentVersion) : DomainEvent;

public sealed record SatelliteDisconnectedDomainEvent(VmId VmId, SatelliteId SatelliteId, string? Reason) : DomainEvent;
