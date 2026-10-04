using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels;

public enum TunnelIngressPlanAction
{
    NoOp,
    Insert,
    Replace,
    Remove
}

public sealed record TunnelIngressPlan(
    TunnelIngressPlanAction Action,
    int? Index,
    IReadOnlyList<TunnelIngressRule> Ingress)
{
    public bool ShouldPut => Action is not TunnelIngressPlanAction.NoOp;
}

/// <summary>
/// OT-008 F1: pure planner for read-modify-write tunnel ingress updates. Runtime callers are added in
/// later tramos so the central path can stay byte-equivalent until the Direct branch exists.
/// </summary>
public static class TunnelIngressPlanner
{
    public static TunnelIngressPlan Plan(
        IReadOnlyList<TunnelIngressRule> current,
        string host,
        string service,
        bool noTlsVerify = false)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var desired = new TunnelIngressRule(host.Trim(), service.Trim(), noTlsVerify);
        var firstMatch = FindHostIndex(current, desired.Hostname!);
        if (firstMatch >= 0)
        {
            var existing = current[firstMatch];
            var hasDuplicate = current
                .Skip(firstMatch + 1)
                .Any(r => SameHost(r.Hostname, desired.Hostname));
            if (!hasDuplicate && SameRule(existing, desired))
            {
                return new TunnelIngressPlan(TunnelIngressPlanAction.NoOp, null, current);
            }

            var replaced = ReplaceHost(current, desired, firstMatch);
            return new TunnelIngressPlan(TunnelIngressPlanAction.Replace, firstMatch, replaced);
        }

        var insertAt = FirstCatchAllIndex(current);
        if (insertAt < 0)
        {
            insertAt = current.Count;
        }

        var planned = current.ToList();
        planned.Insert(insertAt, desired);
        return new TunnelIngressPlan(TunnelIngressPlanAction.Insert, insertAt, planned);
    }

    public static TunnelIngressPlan PlanRemove(IReadOnlyList<TunnelIngressRule> current, string host)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var normalizedHost = host.Trim();
        var firstMatch = FindHostIndex(current, normalizedHost);
        if (firstMatch < 0)
        {
            return new TunnelIngressPlan(TunnelIngressPlanAction.NoOp, null, current);
        }

        var kept = current
            .Where(r => !SameHost(r.Hostname, normalizedHost))
            .ToList();
        return new TunnelIngressPlan(TunnelIngressPlanAction.Remove, firstMatch, kept);
    }

    private static List<TunnelIngressRule> ReplaceHost(
        IReadOnlyList<TunnelIngressRule> current,
        TunnelIngressRule desired,
        int firstMatch)
    {
        var planned = new List<TunnelIngressRule>(current.Count);
        for (var i = 0; i < current.Count; i++)
        {
            if (!SameHost(current[i].Hostname, desired.Hostname))
            {
                planned.Add(current[i]);
                continue;
            }

            if (i == firstMatch)
            {
                planned.Add(desired);
            }
        }

        return planned;
    }

    private static int FindHostIndex(IReadOnlyList<TunnelIngressRule> rules, string host)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            if (SameHost(rules[i].Hostname, host))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FirstCatchAllIndex(IReadOnlyList<TunnelIngressRule> rules)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].Hostname is null)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool SameRule(TunnelIngressRule left, TunnelIngressRule right)
        => SameHost(left.Hostname, right.Hostname)
            && string.Equals(left.Service, right.Service, StringComparison.Ordinal)
            && left.NoTlsVerify == right.NoTlsVerify;

    private static bool SameHost(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
