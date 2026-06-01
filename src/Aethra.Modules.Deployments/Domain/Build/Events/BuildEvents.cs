using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Deployments.Domain.Build.Events;

/// <summary>
/// Evento de dominio in-module: un build acaba de encolarse. Útil para que otros agregados
/// dentro del mismo bounded context reaccionen sin pasar por el outbox.
/// </summary>
public sealed record BuildQueuedEvent(BuildId BuildId, string TemplateId, string GitSha) : DomainEvent;

/// <summary>
/// Evento de dominio in-module: el build cambió de estado. Lo usa principalmente el
/// orquestador para emitir telemetría/SignalR sin invocar consumidores externos.
/// </summary>
public sealed record BuildStatusChangedDomainEvent(BuildId BuildId, BuildStatus From, BuildStatus To)
    : DomainEvent;

/// <summary>
/// Evento de dominio in-module: el build completó con éxito. El handler equivalente cross-module
/// es <c>BuildCompletedIntegrationEvent</c> en <c>Shared.Contracts.Deployments</c>.
/// </summary>
public sealed record BuildCompletedDomainEvent(BuildId BuildId, string TemplateId, string ImageRef)
    : DomainEvent;

/// <summary>
/// Evento de dominio in-module: el build falló. La transición a <see cref="BuildStatus.Failed"/>
/// también emite un <see cref="BuildStatusChangedDomainEvent"/> previo; este evento añade el
/// código de error y la etapa donde se rompió.
/// </summary>
public sealed record BuildFailedDomainEvent(
    BuildId BuildId,
    string TemplateId,
    BuildStatus FailedAtStage,
    string ErrorCode,
    string ErrorMessage) : DomainEvent;
