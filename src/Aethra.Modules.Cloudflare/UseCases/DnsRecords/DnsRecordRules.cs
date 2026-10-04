using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;

namespace Aethra.Modules.Cloudflare.UseCases.DnsRecords;

public enum DnsRecordRuleAction
{
    Create,
    Ok,
    Conflict
}

public sealed record DnsRecordRuleDecision(
    DnsRecordRuleAction Action,
    string DesiredTarget,
    string? ExistingTarget,
    string? Reason);

/// <summary>
/// OT-008 F1: pure rule for Direct tunnel CNAME ownership. Existing records pointing elsewhere are
/// conflicts so a satellite host cannot silently keep using the old tunnel.
/// </summary>
public static class DnsRecordRules
{
    /// <summary>
    /// OT-008 T14: decides over ALL records of a host. Only address records (CNAME/A/AAAA) count; MX, TXT,
    /// CAA, SRV, NS... coexist legitimately at an apex (mail + flattened tunnel CNAME) and are ignored.
    /// Any address record that is not the correct proxied CNAME is a conflict.
    /// </summary>
    public static DnsRecordRuleDecision ClassifyHost(
        IEnumerable<CloudflareDnsRecordInfo> records, string hostname, string desiredTarget)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredTarget);

        var address = records
            .Where(r => string.Equals(r.Name, hostname, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(r.Type, "CNAME", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Type, "A", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Type, "AAAA", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (address.Count == 0)
        {
            return Classify(null, desiredTarget);
        }

        DnsRecordRuleDecision? ok = null;
        foreach (var record in address)
        {
            var decision = Classify(record, desiredTarget);
            if (decision.Action == DnsRecordRuleAction.Conflict)
            {
                return decision;
            }
            ok ??= decision;
        }
        return ok!;
    }

    public static DnsRecordRuleDecision Classify(CloudflareDnsRecordInfo? existing, string desiredTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredTarget);

        var desired = NormalizeTarget(desiredTarget);
        if (existing is null)
        {
            return new DnsRecordRuleDecision(DnsRecordRuleAction.Create, desired, null, null);
        }

        var existingTarget = NormalizeTarget(existing.Content);
        var isCname = string.Equals(existing.Type, "CNAME", StringComparison.OrdinalIgnoreCase);
        if (isCname
            && existing.Proxied
            && string.Equals(existingTarget, desired, StringComparison.OrdinalIgnoreCase))
        {
            return new DnsRecordRuleDecision(DnsRecordRuleAction.Ok, desired, existingTarget, null);
        }

        var reason = !isCname
            ? "Existing record is not a CNAME."
            : !existing.Proxied
                ? "CNAME is DNS-only; it must be proxied."
                : "CNAME points to a different target.";
        return new DnsRecordRuleDecision(DnsRecordRuleAction.Conflict, desired, existingTarget, reason);
    }

    private static string NormalizeTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return target.Trim().TrimEnd('.');
    }
}
