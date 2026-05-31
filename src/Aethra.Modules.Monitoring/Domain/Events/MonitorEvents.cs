using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Monitoring.Domain.Events;

/// <summary>
/// El monitor acaba de crearse. Útil para que otros módulos (Notes, Dashboard) lo reflejen.
/// </summary>
public sealed record MonitorCreatedEvent(MonitorId MonitorId, string Slug, string Url) : DomainEvent;

/// <summary>
/// El status del monitor cambió tras un check. Se publica solo cuando hay transición efectiva
/// (Up→Down, Down→Up, etc.) — no se emite si la pasada confirma el estado actual. Reduce ruido
/// en outbox/SignalR y permite que listeners reaccionen a "incidentes" reales.
/// </summary>
public sealed record MonitorStatusChangedEvent(
    MonitorId MonitorId,
    MonitorStatus From,
    MonitorStatus To,
    MonitorCheckId CheckId) : DomainEvent;

/// <summary>
/// El monitor fue desactivado por el usuario. El worker dejará de probarlo.
/// </summary>
public sealed record MonitorDisabledEvent(MonitorId MonitorId) : DomainEvent;
