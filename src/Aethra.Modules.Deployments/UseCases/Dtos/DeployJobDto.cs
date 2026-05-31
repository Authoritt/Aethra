namespace Aethra.Modules.Deployments.UseCases.Dtos;

public sealed record DeployJobSummaryDto(
    string Id,
    string ApplicationId,
    string ApplicationSlug,
    string GitSha,
    string ShortSha,
    string Status,
    string Trigger,
    string? TriggeredBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    double? DurationSeconds,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record DeployLogChunkDto(
    string JobId,
    long Sequence,
    DateTimeOffset Timestamp,
    string Level,
    string Stage,
    string Text);
