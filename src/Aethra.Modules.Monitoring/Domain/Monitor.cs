using Aethra.Modules.Monitoring.Domain.Events;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Monitoring.Domain;

/// <summary>
/// Aggregate root de un monitor uptime HTTP. Define cómo (método, headers, body) y cuándo
/// (intervalo, timeout) probar una URL y qué considerar Up/Degraded/Down. Mantiene además
/// el estado consolidado del último check para que la UI no tenga que recalcularlo.
///
/// Convenciones:
/// <list type="bullet">
///   <item>Los valores recibidos se "clampean" para evitar configs absurdas (ej. intervalo de 1s
///         o timeout de 5min sobre un endpoint que solo lo desea para uptime).</item>
///   <item>Las transiciones de estado se concentran en <see cref="RecordCheck"/>; el dominio
///         decide cuándo emitir <see cref="MonitorStatusChangedEvent"/> evitando ruido.</item>
/// </list>
/// </summary>
public sealed class Monitor : AggregateRoot<MonitorId>
{
    public const int MinIntervalSec = 30;
    public const int MaxIntervalSec = 3600;
    public const int MinTimeoutMs = 1000;
    public const int MaxTimeoutMs = 60000;

    public Slug Slug { get; private set; }
    public string Name { get; private set; }
    public string Url { get; private set; }
    public MonitorHttpMethod HttpMethod { get; private set; }

    private List<int> _expectedStatusCodes = [];
    public IReadOnlyList<int> ExpectedStatusCodes => _expectedStatusCodes.AsReadOnly();

    public int IntervalSec { get; private set; }
    public int TimeoutMs { get; private set; }

    private Dictionary<string, string>? _headers;
    public IReadOnlyDictionary<string, string>? Headers
        => _headers is null ? null : _headers.AsReadOnly();

    public string? BodyTemplate { get; private set; }
    public string? ApplicationId { get; private set; }
    public string? ProjectId { get; private set; }
    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public MonitorStatus LastStatus { get; private set; }
    public int ConsecutiveFailures { get; private set; }

    private Monitor(
        MonitorId id,
        Slug slug,
        string name,
        string url,
        MonitorHttpMethod method,
        IReadOnlyList<int> expectedStatusCodes,
        int intervalSec,
        int timeoutMs,
        IReadOnlyDictionary<string, string>? headers,
        string? bodyTemplate,
        string? applicationId,
        string? projectId,
        DateTimeOffset now) : base(id)
    {
        Slug = slug;
        Name = name;
        Url = url;
        HttpMethod = method;
        _expectedStatusCodes = NormalizeExpected(expectedStatusCodes);
        IntervalSec = ClampInterval(intervalSec);
        TimeoutMs = ClampTimeout(timeoutMs);
        _headers = headers is { Count: > 0 } ? new Dictionary<string, string>(headers, StringComparer.Ordinal) : null;
        BodyTemplate = bodyTemplate;
        ApplicationId = applicationId;
        ProjectId = projectId;
        IsEnabled = true;
        CreatedAt = now;
        UpdatedAt = now;
        LastStatus = MonitorStatus.Unknown;
        ConsecutiveFailures = 0;
    }

    /// <summary>
    /// Crea un nuevo monitor uptime. La URL debe ser absoluta http(s); el método debe ser GET/HEAD
    /// (POST permitido solo si se aporta body, no se valida aquí porque el usuario podría enviar
    /// POST con body vacío contra un endpoint que lo acepta).
    /// </summary>
    public static Monitor Create(
        Slug slug,
        string name,
        string url,
        MonitorHttpMethod method,
        IReadOnlyList<int> expectedStatusCodes,
        int intervalSec,
        int timeoutMs,
        IReadOnlyDictionary<string, string>? headers,
        string? bodyTemplate,
        string? applicationId,
        string? projectId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name requerido.", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Url requerida.", nameof(url));
        }
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Url debe ser absoluta http(s)://...", nameof(url));
        }

        var monitor = new Monitor(
            MonitorId.New(),
            slug,
            name.Trim(),
            url.Trim(),
            method,
            expectedStatusCodes,
            intervalSec,
            timeoutMs,
            headers,
            string.IsNullOrWhiteSpace(bodyTemplate) ? null : bodyTemplate,
            string.IsNullOrWhiteSpace(applicationId) ? null : applicationId,
            string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            now);
        monitor.Raise(new MonitorCreatedEvent(monitor.Id, slug.Value, monitor.Url));
        return monitor;
    }

    /// <summary>
    /// Actualiza la configuración del monitor. Valores nulos significan "no cambiar".
    /// El status consolidado y los timestamps de check NO se tocan — esto es solo config.
    /// </summary>
    public void UpdateConfig(
        string? name,
        string? url,
        MonitorHttpMethod? method,
        IReadOnlyList<int>? expectedStatusCodes,
        int? intervalSec,
        int? timeoutMs,
        IReadOnlyDictionary<string, string>? headers,
        bool clearHeaders,
        string? bodyTemplate,
        bool clearBodyTemplate,
        string? applicationId,
        bool clearApplicationId,
        string? projectId,
        bool clearProjectId,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Url debe ser absoluta http(s)://...", nameof(url));
            }
            Url = url.Trim();
        }
        if (method is { } m)
        {
            HttpMethod = m;
        }
        if (expectedStatusCodes is { Count: > 0 })
        {
            _expectedStatusCodes = NormalizeExpected(expectedStatusCodes);
        }
        if (intervalSec is { } iv)
        {
            IntervalSec = ClampInterval(iv);
        }
        if (timeoutMs is { } to)
        {
            TimeoutMs = ClampTimeout(to);
        }
        if (clearHeaders)
        {
            _headers = null;
        }
        else if (headers is { Count: > 0 })
        {
            _headers = new Dictionary<string, string>(headers, StringComparer.Ordinal);
        }
        if (clearBodyTemplate)
        {
            BodyTemplate = null;
        }
        else if (!string.IsNullOrWhiteSpace(bodyTemplate))
        {
            BodyTemplate = bodyTemplate;
        }
        if (clearApplicationId)
        {
            ApplicationId = null;
        }
        else if (!string.IsNullOrWhiteSpace(applicationId))
        {
            ApplicationId = applicationId;
        }
        if (clearProjectId)
        {
            ProjectId = null;
        }
        else if (!string.IsNullOrWhiteSpace(projectId))
        {
            ProjectId = projectId;
        }
        UpdatedAt = now;
    }

    public void Enable(DateTimeOffset now)
    {
        if (IsEnabled)
        {
            return;
        }
        IsEnabled = true;
        UpdatedAt = now;
    }

    public void Disable(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            return;
        }
        IsEnabled = false;
        UpdatedAt = now;
        Raise(new MonitorDisabledEvent(Id));
    }

    /// <summary>
    /// Aplica el resultado de un <see cref="MonitorCheck"/> al estado consolidado.
    /// Reglas:
    /// <list type="bullet">
    ///   <item><c>ConsecutiveFailures</c> aumenta con Down/Degraded, se resetea con Up.</item>
    ///   <item>Si el status nuevo difiere del actual, emite <see cref="MonitorStatusChangedEvent"/>.</item>
    /// </list>
    /// </summary>
    public void RecordCheck(MonitorCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var previous = LastStatus;
        LastStatus = check.Status;
        LastCheckedAt = check.Timestamp;

        ConsecutiveFailures = check.Status switch
        {
            MonitorStatus.Up => 0,
            MonitorStatus.Degraded or MonitorStatus.Down => ConsecutiveFailures + 1,
            _ => ConsecutiveFailures,
        };

        if (previous != check.Status)
        {
            Raise(new MonitorStatusChangedEvent(Id, previous, check.Status, check.Id));
        }
    }

    /// <summary>Útil para tests y para el worker que necesita comprobar si toca disparar otro check.</summary>
    public bool IsDueAt(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            return false;
        }
        if (LastCheckedAt is null)
        {
            return true;
        }
        return (now - LastCheckedAt.Value).TotalSeconds >= IntervalSec;
    }

    private static int ClampInterval(int interval)
        => Math.Clamp(interval, MinIntervalSec, MaxIntervalSec);

    private static int ClampTimeout(int timeout)
        => Math.Clamp(timeout, MinTimeoutMs, MaxTimeoutMs);

    private static List<int> NormalizeExpected(IReadOnlyList<int> codes)
    {
        if (codes is null || codes.Count == 0)
        {
            return [200];
        }
        var distinct = new HashSet<int>();
        foreach (var c in codes)
        {
            if (c is >= 100 and <= 599)
            {
                distinct.Add(c);
            }
        }
        if (distinct.Count == 0)
        {
            distinct.Add(200);
        }
        var list = new List<int>(distinct);
        list.Sort();
        return list;
    }

    // EF Core
    private Monitor() : base()
    {
        Slug = default;
        Name = string.Empty;
        Url = string.Empty;
        _expectedStatusCodes = [];
    }
}
