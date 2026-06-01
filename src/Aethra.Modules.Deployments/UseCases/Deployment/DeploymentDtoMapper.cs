using Aethra.Modules.Deployments.UseCases.Deployment.Dtos;

namespace Aethra.Modules.Deployments.UseCases.Deployment;

/// <summary>
/// Mapeo entre el agregado <see cref="Domain.Deployment.Deployment"/> y los DTOs de salida.
/// Centralizamos la transformación para que el formato (status lower, duration calculada,
/// failed-at-stage normalizado) sea uniforme entre commands/queries.
/// </summary>
internal static class DeploymentDtoMapper
{
    public static DeploymentSummaryDto ToSummary(Domain.Deployment.Deployment d)
    {
        var duration = d is { StartedAt: { } s, FinishedAt: { } f }
            ? (f - s).TotalSeconds
            : (double?)null;
        return new DeploymentSummaryDto(
            Id: d.Id.ToString(),
            BuildId: d.BuildId,
            InstanceId: d.InstanceId,
            Status: d.Status.ToString().ToLowerInvariant(),
            Trigger: d.Trigger.ToString().ToLowerInvariant(),
            TriggeredBy: d.TriggeredBy,
            CreatedAt: d.CreatedAt,
            StartedAt: d.StartedAt,
            FinishedAt: d.FinishedAt,
            DurationSeconds: duration,
            OldContainerId: d.OldContainerId,
            NewContainerId: d.NewContainerId,
            OldImageRef: d.OldImageRef,
            NewImageRef: d.NewImageRef,
            ErrorCode: d.ErrorCode,
            ErrorMessage: d.ErrorMessage,
            FailedAtStage: d.FailedAtStage?.ToString().ToLowerInvariant());
    }
}
