using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.UseCases.Dtos;

namespace Aethra.Modules.Monitoring.UseCases;

internal static class MonitorMapper
{
    public static MonitorSummaryDto ToSummary(Monitor m) => new(
        Id: m.Id.ToString(),
        Slug: m.Slug.Value,
        Name: m.Name,
        Url: m.Url,
        HttpMethod: m.HttpMethod.ToString(),
        IntervalSec: m.IntervalSec,
        TimeoutMs: m.TimeoutMs,
        Status: m.LastStatus.ToString(),
        IsEnabled: m.IsEnabled,
        LastCheckedAt: m.LastCheckedAt,
        ConsecutiveFailures: m.ConsecutiveFailures,
        ApplicationId: m.ApplicationId,
        ProjectId: m.ProjectId);

    public static MonitorDetailDto ToDetail(Monitor m) => new(
        Id: m.Id.ToString(),
        Slug: m.Slug.Value,
        Name: m.Name,
        Url: m.Url,
        HttpMethod: m.HttpMethod.ToString(),
        ExpectedStatusCodes: m.ExpectedStatusCodes,
        IntervalSec: m.IntervalSec,
        TimeoutMs: m.TimeoutMs,
        Headers: m.Headers,
        BodyTemplate: m.BodyTemplate,
        ApplicationId: m.ApplicationId,
        ProjectId: m.ProjectId,
        IsEnabled: m.IsEnabled,
        Status: m.LastStatus.ToString(),
        LastCheckedAt: m.LastCheckedAt,
        ConsecutiveFailures: m.ConsecutiveFailures,
        CreatedAt: m.CreatedAt,
        UpdatedAt: m.UpdatedAt);

    public static MonitorCheckDto ToCheckDto(MonitorCheck c) => new(
        Id: c.Id.ToString(),
        MonitorId: c.MonitorId.ToString(),
        Timestamp: c.Timestamp,
        Status: c.Status.ToString(),
        HttpStatusCode: c.HttpStatusCode,
        LatencyMs: c.LatencyMs,
        ErrorMessage: c.ErrorMessage,
        ResponseSnippet: c.ResponseSnippet);
}
