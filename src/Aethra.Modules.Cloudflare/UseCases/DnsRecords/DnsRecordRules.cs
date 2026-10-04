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
    public static DnsRecordRuleDecision Classify(CloudflareDnsRecordInfo? existing, string desiredTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredTarget);

        var desired = NormalizeTarget(desiredTarget);
        if (existing is null)
        {
            return new DnsRecordRuleDecision(DnsRecordRuleAction.Create, desired, null, null);
        }

        var existingTarget = NormalizeTarget(existing.Content);
        if (string.Equals(existing.Type, "CNAME", StringComparison.OrdinalIgnoreCase)
            && string.Equals(existingTarget, desired, StringComparison.OrdinalIgnoreCase))
        {
            return new DnsRecordRuleDecision(DnsRecordRuleAction.Ok, desired, existingTarget, null);
        }

        var reason = string.Equals(existing.Type, "CNAME", StringComparison.OrdinalIgnoreCase)
            ? "CNAME points to a different target."
            : "Existing record is not a CNAME.";
        return new DnsRecordRuleDecision(DnsRecordRuleAction.Conflict, desired, existingTarget, reason);
    }

    private static string NormalizeTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return target.Trim().TrimEnd('.');
    }
}
