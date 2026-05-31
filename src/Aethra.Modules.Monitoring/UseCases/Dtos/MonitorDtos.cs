namespace Aethra.Modules.Monitoring.UseCases.Dtos;

/// <summary>
/// Resumen ligero para listados (page principal).
/// </summary>
public sealed record MonitorSummaryDto(
    string Id,
    string Slug,
    string Name,
    string Url,
    string HttpMethod,
    int IntervalSec,
    int TimeoutMs,
    string Status,                       // "Unknown" | "Up" | "Down" | "Degraded"
    bool IsEnabled,
    DateTimeOffset? LastCheckedAt,
    int ConsecutiveFailures,
    string? ApplicationId,
    string? ProjectId);

/// <summary>
/// Detalle completo (página individual / API por id).
/// </summary>
public sealed record MonitorDetailDto(
    string Id,
    string Slug,
    string Name,
    string Url,
    string HttpMethod,
    IReadOnlyList<int> ExpectedStatusCodes,
    int IntervalSec,
    int TimeoutMs,
    IReadOnlyDictionary<string, string>? Headers,
    string? BodyTemplate,
    string? ApplicationId,
    string? ProjectId,
    bool IsEnabled,
    string Status,
    DateTimeOffset? LastCheckedAt,
    int ConsecutiveFailures,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Una muestra (check) individual.
/// </summary>
public sealed record MonitorCheckDto(
    string Id,
    string MonitorId,
    DateTimeOffset Timestamp,
    string Status,
    int? HttpStatusCode,
    int? LatencyMs,
    string? ErrorMessage,
    string? ResponseSnippet);

/// <summary>
/// Conteos agregados por status; útil para el dashboard.
/// </summary>
public sealed record MonitorOverviewDto(
    int Total,
    int Up,
    int Down,
    int Degraded,
    int Unknown,
    int Disabled);
