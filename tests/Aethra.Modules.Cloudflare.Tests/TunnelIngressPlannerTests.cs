using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.Tunnels;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

public sealed class TunnelIngressPlannerTests
{
    [Fact]
    public void Plan_inserts_new_host_before_catch_all()
    {
        var current = new[]
        {
            new TunnelIngressRule("api.example.com", "http://api:8080"),
            new TunnelIngressRule(null, "http_status:404")
        };

        var plan = TunnelIngressPlanner.Plan(current, "app.example.com", "http://app:3000");

        plan.Action.Should().Be(TunnelIngressPlanAction.Insert);
        plan.Index.Should().Be(1);
        plan.Ingress.Should().Equal(
            current[0],
            new TunnelIngressRule("app.example.com", "http://app:3000"),
            current[1]);
    }

    [Fact]
    public void Plan_appends_new_host_when_no_catch_all_exists()
    {
        var current = new[] { new TunnelIngressRule("api.example.com", "http://api:8080") };

        var plan = TunnelIngressPlanner.Plan(current, "app.example.com", "http://app:3000");

        plan.Action.Should().Be(TunnelIngressPlanAction.Insert);
        plan.Index.Should().Be(1);
        plan.Ingress.Should().EndWith(new TunnelIngressRule("app.example.com", "http://app:3000"));
    }

    [Fact]
    public void Plan_is_noop_when_host_already_points_to_the_desired_service()
    {
        var current = new[]
        {
            new TunnelIngressRule("APP.example.com", "http://app:3000"),
            new TunnelIngressRule(null, "http_status:404")
        };

        var plan = TunnelIngressPlanner.Plan(current, "app.example.com", "http://app:3000");

        plan.Action.Should().Be(TunnelIngressPlanAction.NoOp);
        plan.ShouldPut.Should().BeFalse();
        plan.Ingress.Should().Equal(current);
    }

    [Fact]
    public void Plan_replaces_host_that_points_to_another_service()
    {
        var current = new[]
        {
            new TunnelIngressRule("app.example.com", "http://old:3000"),
            new TunnelIngressRule(null, "http_status:404")
        };

        var plan = TunnelIngressPlanner.Plan(current, "app.example.com", "http://app:3000");

        plan.Action.Should().Be(TunnelIngressPlanAction.Replace);
        plan.Index.Should().Be(0);
        plan.Ingress.Should().Equal(
            new TunnelIngressRule("app.example.com", "http://app:3000"),
            current[1]);
    }

    [Fact]
    public void Plan_collapses_duplicate_hosts_to_one_desired_rule()
    {
        var current = new[]
        {
            new TunnelIngressRule("app.example.com", "http://old:3000"),
            new TunnelIngressRule("APP.example.com", "http://app:3000"),
            new TunnelIngressRule(null, "http_status:404")
        };

        var plan = TunnelIngressPlanner.Plan(current, "app.example.com", "http://app:3000");

        plan.Action.Should().Be(TunnelIngressPlanAction.Replace);
        plan.Ingress.Should().Equal(
            new TunnelIngressRule("app.example.com", "http://app:3000"),
            current[2]);
    }

    [Fact]
    public void PlanRemove_removes_all_matching_hosts_and_keeps_catch_all()
    {
        var current = new[]
        {
            new TunnelIngressRule("app.example.com", "http://one:3000"),
            new TunnelIngressRule("APP.example.com", "http://two:3000"),
            new TunnelIngressRule(null, "http_status:404")
        };

        var plan = TunnelIngressPlanner.PlanRemove(current, "app.example.com");

        plan.Action.Should().Be(TunnelIngressPlanAction.Remove);
        plan.Index.Should().Be(0);
        plan.Ingress.Should().Equal(current[2]);
    }

    [Fact]
    public void PlanRemove_is_noop_when_host_is_absent()
    {
        var current = new[]
        {
            new TunnelIngressRule("api.example.com", "http://api:8080"),
            new TunnelIngressRule(null, "http_status:404")
        };

        var plan = TunnelIngressPlanner.PlanRemove(current, "app.example.com");

        plan.Action.Should().Be(TunnelIngressPlanAction.NoOp);
        plan.ShouldPut.Should().BeFalse();
        plan.Ingress.Should().Equal(current);
    }
}
