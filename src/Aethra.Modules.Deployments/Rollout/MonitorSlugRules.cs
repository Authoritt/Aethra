namespace Aethra.Modules.Deployments.Rollout;

public sealed record MonitorSlugDecision(string InitialSlug, string DesiredSlug, bool ShouldRetry);

/// <summary>
/// OT-008 F1 — regla pura para el slug de monitor que T5 conectará en
/// <c>NativeDeployRunner.ReconcileRoutingAsync</c> donde hoy se usa <c>host.Split('.')[0]</c> si hay
/// varios hosts y el slug de la instance si solo hay uno (línea histórica 703).
/// </summary>
public static class MonitorSlugRules
{
    /// <summary>
    /// OT-008 T7b (#6/N6) — nº de hosts DESEADOS (distintos, sin distinguir mayúsculas, sin vacíos).
    /// El slug del monitor depende de esto y no de los hosts tocados en esta pasada (en Direct
    /// los hosts "tocados" dependen del éxito de cada ruta).
    /// </summary>
    public static int DesiredHostCount(IEnumerable<string?> desiredHosts)
        => desiredHosts
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    public static string InitialSlug(string instanceSlug, string host, int hostCount)
        => hostCount > 1 ? FirstLabel(host) : NormalizeSlug(instanceSlug);

    public static string RetrySlug(string instanceSlug, string host)
    {
        var slug = NormalizeSlug(instanceSlug);
        var label = FirstLabel(host);
        return string.Equals(slug, label, StringComparison.Ordinal)
            ? slug
            : $"{slug}-{label}";
    }

    public static MonitorSlugDecision Decide(
        string instanceSlug,
        string host,
        int hostCount,
        string desiredUrl,
        string? existingUrlForInitialSlug)
    {
        var initial = InitialSlug(instanceSlug, host, hostCount);
        var shouldRetry = !string.IsNullOrWhiteSpace(existingUrlForInitialSlug)
            && !SameHost(desiredUrl, existingUrlForInitialSlug);

        return shouldRetry
            ? new MonitorSlugDecision(initial, RetrySlug(instanceSlug, host), ShouldRetry: true)
            : new MonitorSlugDecision(initial, initial, ShouldRetry: false);
    }

    private static string FirstLabel(string host)
    {
        var normalized = NormalizeSlug(host);
        var dot = normalized.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? normalized : normalized[..dot];
    }

    private static string NormalizeSlug(string value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "app" : normalized;
    }

    private static bool SameHost(string desiredUrl, string? existingUrl)
    {
        if (string.IsNullOrWhiteSpace(desiredUrl) || string.IsNullOrWhiteSpace(existingUrl))
        {
            return false;
        }

        return Uri.TryCreate(desiredUrl.Trim(), UriKind.Absolute, out var desired)
            && Uri.TryCreate(existingUrl.Trim(), UriKind.Absolute, out var existing)
            && string.Equals(desired.Host, existing.Host, StringComparison.OrdinalIgnoreCase);
    }
}
