using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Monitoring.Domain;

/// <summary>
/// Una muestra del probe HTTP contra un <see cref="Monitor"/>. Append-only: nunca se modifica
/// tras crearse; el historial sirve para gráficos y debug de incidentes.
///
/// <para>
/// <see cref="ResponseSnippet"/> guarda los primeros 200 caracteres del body solo cuando hace
/// falta investigar un Degraded/Down — para Up no se persiste (waste of bytes en una time-series).
/// </para>
/// </summary>
public sealed class MonitorCheck : Entity<MonitorCheckId>
{
    public const int SnippetMaxLength = 200;

    public MonitorId MonitorId { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public MonitorStatus Status { get; private set; }
    public int? HttpStatusCode { get; private set; }
    public int? LatencyMs { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? ResponseSnippet { get; private set; }

    private MonitorCheck(
        MonitorCheckId id,
        MonitorId monitorId,
        DateTimeOffset timestamp,
        MonitorStatus status,
        int? httpStatusCode,
        int? latencyMs,
        string? errorMessage,
        string? responseSnippet) : base(id)
    {
        MonitorId = monitorId;
        Timestamp = timestamp;
        Status = status;
        HttpStatusCode = httpStatusCode;
        LatencyMs = latencyMs;
        ErrorMessage = errorMessage;
        ResponseSnippet = responseSnippet;
    }

    public static MonitorCheck Create(
        MonitorId monitorId,
        DateTimeOffset timestamp,
        MonitorStatus status,
        int? httpStatusCode,
        int? latencyMs,
        string? errorMessage,
        string? responseSnippet)
    {
        if (status == MonitorStatus.Unknown)
        {
            throw new ArgumentException("Un check no puede crearse con status Unknown.", nameof(status));
        }
        var snippet = responseSnippet;
        if (snippet is { Length: > SnippetMaxLength })
        {
            snippet = snippet[..SnippetMaxLength];
        }
        var message = errorMessage;
        if (message is { Length: > 1000 })
        {
            message = message[..1000];
        }
        return new MonitorCheck(
            MonitorCheckId.New(),
            monitorId,
            timestamp,
            status,
            httpStatusCode,
            latencyMs is { } l && l < 0 ? 0 : latencyMs,
            message,
            snippet);
    }

    // EF Core
    private MonitorCheck() : base()
    {
        MonitorId = default;
    }
}
