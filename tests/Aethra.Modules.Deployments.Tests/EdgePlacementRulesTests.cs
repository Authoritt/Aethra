using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

public sealed class EdgePlacementRulesTests
{
    [Fact]
    public void Vm_del_tunel_central_real_sigue_en_central_sin_fallo()
    {
        var decision = EdgePlacementRules.Resolve(
            "vm_ET39W0FFRCCQS60DY90B516MPR",
            [],
            centralProxyVmId: "vm_ET39W0FFRCCQS60DY90B516MPR");

        decision.Kind.Should().Be(EdgePlacementKind.Central);
        decision.Tunnel.Should().BeNull();
        decision.Error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Vm_central_desconocida_no_tumba_el_deploy_de_hoy(string? centralProxyVmId)
    {
        // Sin túnel central registrado o con fila sin TargetVmId (anterior a F13.11): hoy se despliega.
        var decision = EdgePlacementRules.Resolve(
            "vm_ET39W0FFRCCQS60DY90B516MPR",
            [],
            centralProxyVmId);

        decision.Kind.Should().Be(EdgePlacementKind.Central);
        decision.Error.Should().BeNull();
        decision.Warnings.Should().Contain(EdgePlacementRules.MissingDirectTunnelWarning);
    }

    [Fact]
    public void Vm_que_no_es_central_y_no_tiene_direct_falla_antes_de_contenedores()
    {
        var decision = EdgePlacementRules.Resolve(
            "vm_bogota",
            [],
            centralProxyVmId: "vm_ET39W0FFRCCQS60DY90B516MPR");

        decision.Kind.Should().Be(EdgePlacementKind.Failed);
        decision.Tunnel.Should().BeNull();
        decision.Error.Should().Contain("no tiene túnel Direct");
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
