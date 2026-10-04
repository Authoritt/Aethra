using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

public sealed class ServiceHostRulesTests
{
    [Fact]
    public void Host_sin_domain_se_mantiene_identico()
    {
        var resolved = ServiceHostRules.Resolve("api.authoritforge.dev", "app.authoritforge.dev");

        resolved.Host.Should().Be("api.authoritforge.dev");
        resolved.UsesDomainToken.Should().BeFalse();
        resolved.DomainResolved.Should().BeTrue();
    }

    [Fact]
    public void Servicio_sin_hostname_usa_el_hostname_de_la_instancia()
    {
        ServiceHostRules.Resolve(null, "app.authoritforge.dev").Host
            .Should().Be("app.authoritforge.dev");
    }

    [Fact]
    public void Token_domain_usa_custom_o_auto_hostname_de_la_instancia()
    {
        var resolved = ServiceHostRules.Resolve("api.{domain}", "inonetickets.lat");

        resolved.Host.Should().Be("api.inonetickets.lat");
        resolved.UsesDomainToken.Should().BeTrue();
        resolved.DomainResolved.Should().BeTrue();
    }

    [Fact]
    public void Token_domain_sin_hostname_de_instancia_queda_no_resuelto()
    {
        var resolved = ServiceHostRules.Resolve("api.{domain}", null);

        resolved.Host.Should().BeNull();
        resolved.UsesDomainToken.Should().BeTrue();
        resolved.DomainResolved.Should().BeFalse();
    }
}
