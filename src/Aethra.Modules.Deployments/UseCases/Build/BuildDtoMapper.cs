using Aethra.Modules.Deployments.UseCases.Build.Dtos;

namespace Aethra.Modules.Deployments.UseCases.Build;

/// <summary>
/// Mapeo entre el agregado <see cref="Domain.Build.Build"/> y los DTOs de salida.
/// Centralizamos la transformación para que el formato (status lower, short sha, etc.)
/// sea uniforme entre commands/queries.
/// </summary>
internal static class BuildDtoMapper
{
    public static BuildSummaryDto ToSummary(Domain.Build.Build b)
    {
        var shortSha = b.GitSha.Length >= 7 ? b.GitSha[..7] : b.GitSha;
        return new BuildSummaryDto(
            Id: b.Id.ToString(),
            TemplateId: b.TemplateId,
            GitSha: b.GitSha,
            ShortSha: shortSha,
            GitRef: b.GitRef,
            Status: b.Status.ToString().ToLowerInvariant(),
            Trigger: b.Trigger.ToString().ToLowerInvariant(),
            TriggeredBy: b.TriggeredBy,
            CreatedAt: b.CreatedAt,
            StartedAt: b.StartedAt,
            FinishedAt: b.FinishedAt,
            BuildDurationMs: b.BuildDurationMs,
            ImageRef: b.ImageRef,
            ErrorCode: b.ErrorCode,
            ErrorMessage: b.ErrorMessage,
            FailedAtStage: b.FailedAtStage?.ToString().ToLowerInvariant());
    }
}
