namespace Aethra.Modules.Deployments.UseCases.Deployment.Dtos;

/// <summary>
/// Proyección plana de un <see cref="Domain.Deployment.Deployment"/> para listados/UI.
/// </summary>
public sealed record DeploymentSummaryDto(
    string Id,
    string BuildId,
    string InstanceId,
    string Status,
    string Trigger,
    string? TriggeredBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    double? DurationSeconds,
    string? OldContainerId,
    string? NewContainerId,
    string? OldImageRef,
    string NewImageRef,
    string? ErrorCode,
    string? ErrorMessage,
    string? FailedAtStage);

/// <summary>
/// Línea individual del log de un deployment (orden estable por <see cref="Sequence"/>).
/// </summary>
public sealed record DeploymentLogChunkDto(
    string DeploymentId,
    long Sequence,
    DateTimeOffset Timestamp,
    string Level,
    string Stage,
    string Text);
