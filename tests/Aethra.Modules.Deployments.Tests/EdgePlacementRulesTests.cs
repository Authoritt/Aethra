using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

public sealed class EdgePlacementRulesTests
{
    [Fact]
    public void Estado_actual_sin_tunel_direct_resuelve_central_con_warning()
    {
        var decision = EdgePlacementRules.Resolve("vm_principal", []);

        decision.Kind.Should().Be(EdgePlacementKind.Central);
        decision.Tunnel.Should().BeNull();
        decision.Warnings.Should().ContainSingle()
            .Which.Should().Be(EdgePlacementRules.MissingDirectTunnelWarning);
    }

    [Fact]
    public void Un_tunel_direct_para_la_vm_resuelve_direct()
    {
        var tunnel = new EdgeTunnelCandidate("tun_bogota", "vm_bogota");

        var decision = EdgePlacementRules.Resolve("vm_bogota", [tunnel]);

        decision.Kind.Should().Be(EdgePlacementKind.Direct);
        decision.Tunnel.Should().Be(tunnel);
        decision.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Mas_de_un_tunel_direct_para_la_misma_vm_es_ambiguo()
    {
        var decision = EdgePlacementRules.Resolve(
            "vm_bogota",
            [
                new EdgeTunnelCandidate("tun_a", "vm_bogota"),
                new EdgeTunnelCandidate("tun_b", "vm_bogota"),
            ]);

        decision.Kind.Should().Be(EdgePlacementKind.Ambiguous);
        decision.Tunnel.Should().BeNull();
    }

    [Fact]
    public void Direct_solo_admite_path_prefix_raiz()
    {
        var result = EdgePlacementRules.ValidateDirect(
            [new DirectRouteCandidate("api", true, "api.example.com", ["/api"])]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("PathPrefix '/'");
    }

    [Fact]
    public void Direct_exige_hostname_resuelto_para_servicios_publicos()
    {
        var result = EdgePlacementRules.ValidateDirect(
            [new DirectRouteCandidate("api", true, null, ["/"])]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("hostname resuelto");
    }
}
