namespace Aethra.Shared.Contracts.Deployments;

/// <summary>
/// Evento cross-module: un build terminó OK y la imagen quedó publicada en el registry.
/// El consumidor principal en F9.4+ será el orquestador de Deployment, que aplicará
/// la nueva imagen a las Instances asociadas al Template.
/// </summary>
/// <param name="GitRef">F12.3 — git ref del build (<c>refs/heads/...</c> o <c>refs/pull/N/head</c>).
/// El handler de fan-out lo usa para filtrar qué Instances redeployar (branch-per-instance).
/// Nullable para compatibilidad con builds legacy persistidos sin este campo.</param>
public sealed record BuildCompletedIntegrationEvent(
    string BuildId,
    string TemplateId,
    string ImageRef,
    string GitSha,
    DateTimeOffset CompletedAt,
    string? GitRef = null) : IntegrationEvent;

/// <summary>
/// Evento cross-module: un build falló y no produjo imagen. Útil para que la UI/Notifier
/// emita la alerta correspondiente sin necesidad de polling.
/// </summary>
public sealed record BuildFailedIntegrationEvent(
    string BuildId,
    string TemplateId,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset FailedAt) : IntegrationEvent;
