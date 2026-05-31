using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Proxy.Domain.Events;

public sealed record RouteAddedEvent(RouteId RouteId, string Hostname, string BackendUrl, bool TlsEnabled) : DomainEvent;

public sealed record RouteUpdatedEvent(RouteId RouteId, string Hostname, string BackendUrl, bool TlsEnabled) : DomainEvent;

public sealed record RouteRemovedEvent(RouteId RouteId, string Hostname) : DomainEvent;
