namespace Aethra.Modules.Deployments.UseCases.Build.Dtos;

/// <summary>
/// Proyección plana de un <see cref="Domain.Build.Build"/> para listados/UI.
/// </summary>
public sealed record BuildSummaryDto(
    string Id,
    string TemplateId,
    string GitSha,
    string ShortSha,
    string GitRef,
    string Status,
    string Trigger,
    string? TriggeredBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long? BuildDurationMs,
    string? ImageRef,
    string? ErrorCode,
    string? ErrorMessage,
    string? FailedAtStage);

/// <summary>
/// Línea individual del log de un build (orden estable por <see cref="Sequence"/>).
/// </summary>
public sealed record BuildLogChunkDto(
    string BuildId,
    long Sequence,
    DateTimeOffset Timestamp,
    string Level,
    string Stage,
    string Text);
