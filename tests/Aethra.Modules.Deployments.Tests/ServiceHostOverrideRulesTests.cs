using Aethra.Modules.Deployments.Rollout;
using Aethra.Shared.Contracts.Projects;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// OT-009 — único punto de resolución del host efectivo de un servicio
/// (<c>override de la instancia ?? svc.Hostname interpolado ?? host de la instancia</c>).
/// <c>NativeDeployRunner.ReconcileRoutingAsync</c> recorre <see cref="ServiceHostRules.PlanPublicEndpoints"/>:
/// la ruta del proxy (paso 1), el ingress Direct + CNAME (paso 2, <c>EnsureDnsRecordAsync</c> sobre
/// <c>hostsTouched</c>), la limpieza de ingress obsoleto (paso 3, contra el set deseado) y el monitor
/// (paso 4) usan TODOS <see cref="ServicePublicEndpoint.Host"/>; la validación Direct usa
/// <see cref="ServiceHostRules.ResolveEffective"/> + <see cref="ServiceHostRules.PublicPrefixes"/>.
/// </summary>
public sealed class ServiceHostOverrideRulesTests
{
    // Topología real de la plantilla inonetickets (tpl_G44A20EM9DC7HCBXAPA6VYDFA8).
    private static readonly IReadOnlyList<TemplateServiceView> InOneTickets =
    [
        Svc("publica", ["/"]),
        Svc("api", [], "api.{domain}"),
        Svc("org", [], "org.{domain}"),
        Svc("admin", [], "admin.{domain}"),
    ];

    // Forma de las 7 apps vivas: rutas por path bajo el host de la instancia, un servicio interno,
    // y un multi-host con hostname literal.
    private static readonly IReadOnlyList<TemplateServiceView> Hermanos =
    [
        Svc("backend", ["/api", "/hubs"]),
        Svc("frontend", ["/"]),
        Svc("worker", []),
        Svc("tenant", [], "tenant.authoritforge.dev"),
    ];

    private static readonly Dictionary<string, string> StagingOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["api"] = "pruebas-api.inonetickets.lat",
        ["org"] = "pruebas-org.inonetickets.lat",
        ["admin"] = "pruebas-admin.inonetickets.lat",
    };

    [Theory]
    [InlineData("inonetickets.lat")]
    [InlineData(null)]
    public void Sin_override_el_plan_es_identico_al_algoritmo_historico_InOneTickets(string? domain)
    {
        AssertSameAsLegacy(InOneTickets, domain);
    }

    [Theory]
    [InlineData("yunke.authoritforge.dev")]
    [InlineData(null)]
    public void Sin_override_el_plan_es_identico_al_algoritmo_historico_hermanos(string? domain)
    {
        AssertSameAsLegacy(Hermanos, domain);
    }

    [Fact]
    public void Sin_override_prod_publica_los_hosts_de_hoy()
    {
        var plan = ServiceHostRules.PlanPublicEndpoints(InOneTickets, "inonetickets.lat", overrides: null);

        plan.Select(e => (e.Service.Name, e.Host, e.FromOverride)).Should().Equal(
            ("publica", "inonetickets.lat", false),
            ("api", "api.inonetickets.lat", false),
            ("org", "org.inonetickets.lat", false),
            ("admin", "admin.inonetickets.lat", false));
    }

    [Fact]
    public void Con_override_staging_publica_pruebas_guion_svc_y_la_raiz_en_su_dominio()
    {
        var plan = ServiceHostRules.PlanPublicEndpoints(InOneTickets, "pruebas.inonetickets.lat", StagingOverrides);

        plan.Select(e => (e.Service.Name, e.Host, e.FromOverride)).Should().Equal(
            ("publica", "pruebas.inonetickets.lat", false),
            ("api", "pruebas-api.inonetickets.lat", true),
            ("org", "pruebas-org.inonetickets.lat", true),
            ("admin", "pruebas-admin.inonetickets.lat", true));
        // Nada de dos niveles (fuera del certificado Universal *.inonetickets.lat).
        plan.Should().NotContain(e => e.Host.EndsWith(".pruebas.inonetickets.lat", StringComparison.Ordinal));
        // Un host propio sin PathPrefixes sigue siendo "/" (una regla de ingress Direct por host).
        plan.Should().OnlyContain(e => e.PathPrefixes.Count == 1 && e.PathPrefixes[0] == "/");
    }

    [Fact]
    public void Con_override_la_validacion_Direct_ve_el_host_del_override()
    {
        var candidates = InOneTickets.Select(s => new DirectRouteCandidate(
            s.Name,
            ServiceHostRules.PublicPrefixes(s, StagingOverrides).Count > 0,
            ServiceHostRules.ResolveEffective(s.Name, s.Hostname, "pruebas.inonetickets.lat", StagingOverrides).Host,
            ServiceHostRules.PublicPrefixes(s, StagingOverrides)));

        candidates.Select(c => c.ResolvedHost).Should().Equal(
            "pruebas.inonetickets.lat",
            "pruebas-api.inonetickets.lat",
            "pruebas-org.inonetickets.lat",
            "pruebas-admin.inonetickets.lat");
        EdgePlacementRules.ValidateDirect(candidates).IsValid.Should().BeTrue();
    }

    [Fact]
    public void El_override_gana_aunque_la_instancia_no_tenga_dominio()
    {
        // {domain} sin host de instancia queda sin resolver (no publica); el override no depende de él.
        var plan = ServiceHostRules.PlanPublicEndpoints(InOneTickets, null, StagingOverrides);

        plan.Select(e => e.Host).Should().Equal(
            "pruebas-api.inonetickets.lat", "pruebas-org.inonetickets.lat", "pruebas-admin.inonetickets.lat");
    }

    [Fact]
    public void La_clave_del_servicio_es_case_insensitive_y_el_host_se_normaliza()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal) { ["API"] = " Pruebas-Api.InOneTickets.lat " };

        var resolved = ServiceHostRules.ResolveEffective("api", "api.{domain}", "pruebas.inonetickets.lat", overrides);

        resolved.Host.Should().Be("pruebas-api.inonetickets.lat");
        resolved.FromOverride.Should().BeTrue();
        resolved.UsesDomainToken.Should().BeFalse();
    }

    [Fact]
    public void Override_en_un_servicio_sin_hostname_ni_prefijos_lo_publica_en_raiz()
    {
        var overrides = new Dictionary<string, string> { ["worker"] = "worker-stg.authoritforge.dev" };

        ServiceHostRules.HasOwnHost(Hermanos[2], overrides).Should().BeTrue();
        ServiceHostRules.PlanPublicEndpoints(Hermanos, "app.authoritforge.dev", overrides)
            .Should().ContainSingle(e => e.Service.Name == "worker")
            .Which.PathPrefixes.Should().Equal("/");
    }

    [Fact]
    public void Un_override_de_un_servicio_que_el_template_no_define_se_reporta_y_no_se_aplica()
    {
        var overrides = new Dictionary<string, string>
        {
            ["api"] = "pruebas-api.inonetickets.lat",
            ["tienda"] = "pruebas-tienda.inonetickets.lat",
        };

        ServiceHostRules.UnmatchedOverrides(InOneTickets, overrides).Should().Equal("tienda");
        ServiceHostRules.UnmatchedOverrides(InOneTickets, null).Should().BeEmpty();
        ServiceHostRules.PlanPublicEndpoints(InOneTickets, "pruebas.inonetickets.lat", overrides)
            .Should().NotContain(e => e.Host == "pruebas-tienda.inonetickets.lat");
    }

    [Fact]
    public void Diccionario_vacio_equivale_a_sin_override()
    {
        var empty = new Dictionary<string, string>();
        ServiceHostRules.PlanPublicEndpoints(InOneTickets, "inonetickets.lat", empty)
            .Should().BeEquivalentTo(ServiceHostRules.PlanPublicEndpoints(InOneTickets, "inonetickets.lat", null),
                o => o.WithStrictOrdering());
    }

    /// <summary>
    /// Réplica literal del bucle de <c>NativeDeployRunner.ReconcileRoutingAsync</c> en <c>29d0aed</c>
    /// (antes de OT-009): <c>ServiceHostRules.Resolve(svc.Hostname, instanceHostname)</c> + el
    /// <c>ResolvePublicPrefixes</c> privado que se retiró. Es la vara de "sin override = hoy".
    /// </summary>
    private static List<(string Service, string Host, IReadOnlyList<string> Prefixes)> LegacyPlan(
        IReadOnlyList<TemplateServiceView> services, string? instanceHostname)
    {
        var result = new List<(string, string, IReadOnlyList<string>)>();
        foreach (var svc in services)
        {
            var host = ServiceHostRules.Resolve(svc.Hostname, instanceHostname).Host;
            if (string.IsNullOrWhiteSpace(host))
            {
                continue;
            }
            IReadOnlyList<string> prefixes = svc.PathPrefixes.Count > 0
                ? svc.PathPrefixes
                : (!string.IsNullOrWhiteSpace(svc.Hostname) ? ["/"] : svc.PathPrefixes);
            if (prefixes.Count == 0)
            {
                continue;
            }
            result.Add((svc.Name, host, prefixes));
        }
        return result;
    }

    private static void AssertSameAsLegacy(IReadOnlyList<TemplateServiceView> services, string? domain)
    {
        var plan = ServiceHostRules.PlanPublicEndpoints(services, domain, overrides: null);
        var legacy = LegacyPlan(services, domain);

        plan.Select(e => (e.Service.Name, e.Host, string.Join(",", e.PathPrefixes)))
            .Should().Equal(legacy.Select(l => (l.Service, l.Host, string.Join(",", l.Prefixes))));
        plan.Should().NotContain(e => e.FromOverride);
        foreach (var svc in services)
        {
            ServiceHostRules.HasOwnHost(svc, null).Should().Be(!string.IsNullOrWhiteSpace(svc.Hostname));
            ServiceHostRules.ResolveEffective(svc.Name, svc.Hostname, domain, null)
                .Should().Be(ServiceHostRules.Resolve(svc.Hostname, domain));
        }
    }

    private static TemplateServiceView Svc(string name, IReadOnlyList<string> prefixes, string? hostname = null)
        => new(name, string.Empty, 8080, prefixes, [], "git", Hostname: hostname);
}
