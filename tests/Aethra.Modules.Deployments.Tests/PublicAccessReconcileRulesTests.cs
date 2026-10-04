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

    // OT-008 H3 - Direct jamas llega a EnsureTunnelHostnameCommand.
    [Theory]
    [InlineData(true, true, TunnelIngressStep.DirectSkip)]
    [InlineData(true, false, TunnelIngressStep.DirectBlockedRedeploy)]
    [InlineData(false, true, TunnelIngressStep.AlreadyPresent)]
    [InlineData(false, false, TunnelIngressStep.EnsureIngress)]
    public void PlanTunnelIngress_solo_asegura_ingress_en_central(bool isDirect, bool hostHasIngress, TunnelIngressStep expected)
    {
        PublicAccessReconcileRules.PlanTunnelIngress(isDirect, hostHasIngress).Should().Be(expected);
    }

    // OT-008 H1 - una VM que lanza al resolverse no tumba la vista.
    [Fact]
    public async Task TryResolve_convierte_una_excepcion_en_fallo_sin_propagarla()
    {
        var result = await SafeResolveRules.TryResolveAsync<int>(
            () => throw new InvalidOperationException("token no descifra"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("token no descifra");
    }

    [Fact]
    public async Task TryResolve_devuelve_el_valor_cuando_no_falla()
    {
        var result = await SafeResolveRules.TryResolveAsync(() => Task.FromResult(7), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be(7);
    }

    [Fact]
    public async Task TryResolve_propaga_la_cancelacion_real()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await SafeResolveRules.TryResolveAsync<int>(
            () => throw new OperationCanceledException(cts.Token), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // Guardas de CABLEADO: apps/api no tiene proyecto de tests, asi que se fija que el endpoint y el runner
    // SIGAN llamando a las reglas puras (si alguien revierte la llamada, estos tests fallan).
    [Theory]
    [InlineData("apps/api/Bootstrap/OperationsEndpoints.cs", "PublicAccessReconcileRules.PlanTunnelIngress(")]
    [InlineData("apps/api/Bootstrap/OperationsEndpoints.cs", "SafeResolveRules.TryResolveAsync(")]
    [InlineData("apps/api/Bootstrap/OperationsEndpoints.cs", "FailIfIngressUnreadable: strictIngress")]
    [InlineData("apps/api/Bootstrap/OperationsEndpoints.cs", "strictIngress: true")]
    [InlineData("apps/api/Bootstrap/NativeDeployRunner.cs", "MonitorSlugRules.DesiredHostCount(desired.Select(")]
    [InlineData("apps/api/Bootstrap/NativeDeployRunner.cs", "MonitorSlugRules.Decide(slug, host, desiredHostCount,")]
    public void El_endpoint_y_el_runner_llaman_a_las_reglas_puras(string relativePath, string expectedCall)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aethra.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("el test necesita localizar la raiz del repo");
        File.ReadAllText(Path.Combine(dir!.FullName, relativePath)).Should().Contain(expectedCall);
    }
}
