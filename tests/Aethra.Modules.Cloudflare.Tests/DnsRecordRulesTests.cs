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

    private const string Apex = "inonetickets.lat";
    private const string Tunnel = "55ef4a72-2acd-4b8c-a30b-9650581a21e9.cfargotunnel.com";

    private static CloudflareDnsRecordInfo Rec(string type, string content, bool proxied = false) =>
        new("rec", type, Apex, content, 1, proxied, null);

    [Fact]
    public void ClassifyHost_creates_when_apex_has_only_mx_and_txt()
    {
        var records = new[]
        {
            Rec("MX", "route1.mx.cloudflare.net"), Rec("MX", "route2.mx.cloudflare.net"),
            Rec("MX", "route3.mx.cloudflare.net"), Rec("TXT", "v=spf1 -all"), Rec("TXT", "other")
        };

        DnsRecordRules.ClassifyHost(records, Apex, Tunnel).Action.Should().Be(DnsRecordRuleAction.Create);
    }

    [Fact]
    public void ClassifyHost_ok_when_apex_has_mail_records_and_the_correct_proxied_cname()
    {
        var records = new[]
        {
            Rec("MX", "route1.mx.cloudflare.net"), Rec("MX", "route2.mx.cloudflare.net"),
            Rec("MX", "route3.mx.cloudflare.net"), Rec("TXT", "v=spf1 -all"), Rec("TXT", "other"),
            Rec("CNAME", Tunnel, proxied: true)
        };

        DnsRecordRules.ClassifyHost(records, Apex, Tunnel).Action.Should().Be(DnsRecordRuleAction.Ok);
    }

    [Fact]
    public void ClassifyHost_conflicts_when_mx_coexists_with_an_a_record()
    {
        var records = new[] { Rec("MX", "route1.mx.cloudflare.net"), Rec("A", "203.0.113.10", proxied: true) };

        DnsRecordRules.ClassifyHost(records, Apex, Tunnel).Action.Should().Be(DnsRecordRuleAction.Conflict);
    }

    [Fact]
    public void ClassifyHost_conflicts_when_correct_cname_coexists_with_aaaa()
    {
        var records = new[] { Rec("CNAME", Tunnel, proxied: true), Rec("AAAA", "2001:db8::1", proxied: true) };

        DnsRecordRules.ClassifyHost(records, Apex, Tunnel).Action.Should().Be(DnsRecordRuleAction.Conflict);
    }

    [Fact]
    public void ClassifyHost_conflicts_when_cname_is_dns_only_even_with_mail_records()
    {
        var records = new[] { Rec("MX", "route1.mx.cloudflare.net"), Rec("CNAME", Tunnel, proxied: false) };

        DnsRecordRules.ClassifyHost(records, Apex, Tunnel).Action.Should().Be(DnsRecordRuleAction.Conflict);
    }

    [Fact]
    public void ClassifyHost_creates_when_only_txt_and_ignores_other_hosts()
    {
        var records = new[]
        {
            Rec("TXT", "v=spf1 -all"),
            new CloudflareDnsRecordInfo("x", "A", "other.inonetickets.lat", "203.0.113.10", 1, true, null)
        };

        DnsRecordRules.ClassifyHost(records, Apex, Tunnel).Action.Should().Be(DnsRecordRuleAction.Create);
    }

    [Fact]
    public void Classify_rejects_blank_desired_target()
    {
        var act = () => DnsRecordRules.Classify(null, "   ");

        act.Should().Throw<ArgumentException>();
    }
}
