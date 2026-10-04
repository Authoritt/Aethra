using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

public sealed class PublicAccessReconcileRulesTests
{
    [Fact]
    public void Host_direct_con_ingress_existente_no_se_reescribe()
    {
        var decision = PublicAccessReconcileRules.DecideDirectIngress(true, "iot-stg.example.com", "tun-1");

        decision.Action.Should().Be(DirectIngressAction.Skip);
    }

    [Fact]
    public void Host_direct_sin_ingress_bloquea_y_manda_a_redesplegar()
    {
        var decision = PublicAccessReconcileRules.DecideDirectIngress(false, "iot-stg.example.com", "tun-1");

        decision.Action.Should().Be(DirectIngressAction.BlockedRedeploy);
        decision.Message.Should().Contain("redesplegando").And.Contain("iot-stg.example.com");
    }

    [Fact]
    public void Sin_tunel_direct_la_infra_central_se_conserva_sin_cambios()
    {
        var choice = PublicAccessReconcileRules.ChooseInfra(null, "central.cfargotunnel.com");

        choice.IsDirect.Should().BeFalse();
        choice.TunnelCname.Should().Be("central.cfargotunnel.com");
    }

    [Fact]
    public void Con_tunel_direct_el_cname_es_el_del_tunel_direct()
    {
        var choice = PublicAccessReconcileRules.ChooseInfra(" tun-bog ", "central.cfargotunnel.com");

        choice.IsDirect.Should().BeTrue();
        choice.TunnelCname.Should().Be("tun-bog.cfargotunnel.com");
    }

    [Fact]
    public void Conteo_de_hosts_deseados_ignora_vacios_y_mayusculas_y_no_depende_de_los_tocados()
    {
        // En Direct los hosts "tocados" dependen del exito de cada ruta; el conteo debe ser el deseado.
        MonitorSlugRules.DesiredHostCount(["a.example.com", "A.example.com", "b.example.com", "", null])
            .Should().Be(2);
        MonitorSlugRules.DesiredHostCount([]).Should().Be(0);
    }

    [Fact]
    public void Con_dos_hosts_deseados_pero_uno_tocado_el_slug_inicial_es_el_label_del_host()
    {
        var desired = MonitorSlugRules.DesiredHostCount(["a.example.com", "b.example.com"]);

        MonitorSlugRules.Decide("yunke", "a.example.com", desired, "https://a.example.com/", null)
            .InitialSlug.Should().Be("a");
    }
}
