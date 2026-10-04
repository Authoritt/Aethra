namespace Aethra.Modules.Deployments.Rollout;

public sealed record ServiceHostResolution(string? Host, bool UsesDomainToken, bool DomainResolved);

/// <summary>
/// OT-008 F1 — resolución pura del host público que T5 insertará en
/// <c>NativeDeployRunner.ReconcileRoutingAsync</c> donde hoy se elige <c>svc.Hostname ?? instanceHostname</c>
/// (línea histórica 606).
/// </summary>
public static class ServiceHostRules
{
    public const string DomainToken = "{domain}";

    public static ServiceHostResolution Resolve(string? serviceHostname, string? instanceHostname)
    {
        var instanceHost = Normalize(instanceHostname);
        var serviceHost = Normalize(serviceHostname);
        if (serviceHost is null)
        {
            return new ServiceHostResolution(instanceHost, UsesDomainToken: false, DomainResolved: true);
        }

        var usesDomain = serviceHost.Contains(DomainToken, StringComparison.Ordinal);
        if (!usesDomain)
        {
            return new ServiceHostResolution(serviceHost, UsesDomainToken: false, DomainResolved: true);
        }

        if (instanceHost is null)
        {
            return new ServiceHostResolution(null, UsesDomainToken: true, DomainResolved: false);
        }

        return new ServiceHostResolution(
            serviceHost.Replace(DomainToken, instanceHost, StringComparison.Ordinal),
            UsesDomainToken: true,
            DomainResolved: true);
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
