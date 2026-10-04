using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

public sealed class DnsRecordRulesTests
{
    [Fact]
    public void Classify_creates_when_no_record_exists()
    {
        var decision = DnsRecordRules.Classify(null, "abc.cfargotunnel.com");

        decision.Action.Should().Be(DnsRecordRuleAction.Create);
        decision.ExistingTarget.Should().BeNull();
    }

    [Fact]
    public void Classify_accepts_cname_to_the_desired_tunnel()
    {
        var existing = new CloudflareDnsRecordInfo(
            "rec-1", "CNAME", "app.example.com", "ABC.cfargotunnel.com.", 1, true, null);

        var decision = DnsRecordRules.Classify(existing, "abc.cfargotunnel.com");

        decision.Action.Should().Be(DnsRecordRuleAction.Ok);
        decision.Reason.Should().BeNull();
    }

    [Fact]
    public void Classify_conflicts_when_matching_cname_is_dns_only()
    {
        var existing = new CloudflareDnsRecordInfo(
            "rec-1", "CNAME", "app.example.com", "abc.cfargotunnel.com", 1, false, null);

        var decision = DnsRecordRules.Classify(existing, "abc.cfargotunnel.com");

        decision.Action.Should().Be(DnsRecordRuleAction.Conflict);
        decision.Reason.Should().Contain("DNS-only");
    }

    [Fact]
    public void Classify_conflicts_when_cname_points_to_another_tunnel()
    {
        var existing = new CloudflareDnsRecordInfo(
            "rec-1", "CNAME", "app.example.com", "old.cfargotunnel.com", 1, true, null);

        var decision = DnsRecordRules.Classify(existing, "new.cfargotunnel.com");

        decision.Action.Should().Be(DnsRecordRuleAction.Conflict);
        decision.Reason.Should().Contain("different target");
    }

    [Fact]
    public void Classify_conflicts_when_existing_record_is_not_cname()
    {
        var existing = new CloudflareDnsRecordInfo(
            "rec-1", "A", "app.example.com", "203.0.113.10", 300, false, null);

        var decision = DnsRecordRules.Classify(existing, "abc.cfargotunnel.com");

        decision.Action.Should().Be(DnsRecordRuleAction.Conflict);
        decision.Reason.Should().Contain("not a CNAME");
    }

    [Fact]
    public void Classify_rejects_blank_desired_target()
    {
        var act = () => DnsRecordRules.Classify(null, "   ");

        act.Should().Throw<ArgumentException>();
    }
}
