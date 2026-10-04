using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

public sealed class MonitorSlugRulesTests
{
    [Fact]
    public void Un_solo_host_mantiene_el_slug_actual_de_la_instancia()
        => MonitorSlugRules.InitialSlug("yunke", "api.yunke.dev", hostCount: 1)
            .Should().Be("yunke");

    [Fact]
    public void Varios_hosts_mantienen_el_label_del_host_como_slug_inicial()
        => MonitorSlugRules.InitialSlug("yunke", "api.yunke.dev", hostCount: 2)
            .Should().Be("api");

    [Fact]
    public void Slug_tomado_por_un_monitor_del_mismo_host_no_cambia()
    {
        var decision = MonitorSlugRules.Decide(
            "yunke",
            "api.yunke.dev",
            hostCount: 2,
            desiredUrl: "https://api.yunke.dev/",
            existingUrlForInitialSlug: "https://api.yunke.dev/health");

        decision.Should().Be(new MonitorSlugDecision("api", "api", ShouldRetry: false));
    }

    [Fact]
    public void Slug_tomado_por_otro_host_reintenta_con_slug_mas_label()
    {
        var decision = MonitorSlugRules.Decide(
            "yunke",
            "api.yunke.dev",
            hostCount: 2,
            desiredUrl: "https://api.yunke.dev/",
            existingUrlForInitialSlug: "https://otra-app.dev/");

        decision.Should().Be(new MonitorSlugDecision("api", "yunke-api", ShouldRetry: true));
    }
}
