using Aethra.Modules.Proxy.UseCases.Dtos;
using Aethra.Modules.Proxy.UseCases.Routes;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Proxy.Tests;

/// <summary>
/// OT-001 — fija la regla que <see cref="RouteOwnershipRules.IsObsoleteOwnRoute"/> aplica en el
/// reconciliador del deploy nativo (<c>NativeDeployRunner.ReconcileRoutingAsync</c>, paso 3): un
/// deploy solo borra rutas que ÉL MISMO creó (<c>Origin="native_deploy"</c>), nunca una que comparte
/// backend pero fue creada por fuera. Reproduce el incidente real: los alias <c>yunke-*</c>
/// (Origin="manual") apuntaban al mismo contenedor <c>factusforge-api</c> que la Instance
/// "factusforge", y el reconciliador (que antes solo miraba el backend) los borraba en cada redeploy
/// — confirmado en logs de prod (`reconcile-routing factusforge: ruta obsoleta borrada
/// yunke-*.authoritforge.dev/`, 2026-08-07 20:15:44-46 UTC, deploy nativo completo de 4 servicios
/// disparado por el webhook del push `main@2deddd4`).
/// </summary>
public sealed class RouteOwnershipRulesTests
{
    private static readonly IReadOnlyList<string> MyBackends = ["http://factusforge-api:"];
    private static readonly HashSet<(string Host, string Prefix)> DesiredEmpty = [];

    private static RouteDto Route(
        string hostname, string backendUrl, string? origin, string pathPrefix = "/") => new(
        Id: "rt_test",
        Hostname: hostname,
        PathPrefix: pathPrefix,
        BackendUrl: backendUrl,
        TlsEnabled: false,
        CertStatus: "none",
        CertExpiresAt: null,
        OperationalOwnerType: "app_environment",
        OperationalOwnerId: "ins_test",
        Origin: origin,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    // ---- La regla que exige la OT, palabra por palabra ----

    [Fact]
    public void Una_ruta_con_Origin_ajeno_hacia_el_mismo_backend_sobrevive()
    {
        // El caso real: alias "yunke-api" apuntando al mismo contenedor que "factusforge-api",
        // creado a mano (Origin="manual"), bajo un host que NO está en el set deseado del deploy.
        var ruta = Route("yunke-api.authoritforge.dev", "http://factusforge-api:8080", origin: "manual");

        RouteOwnershipRules.IsObsoleteOwnRoute(ruta, MyBackends, DesiredEmpty).Should().BeFalse();
    }

    [Fact]
    public void Una_ruta_con_Origin_native_deploy_bajo_un_host_viejo_se_borra()
    {
        var ruta = Route("factusforge-old.authoritforge.dev", "http://factusforge-api:8080", origin: "native_deploy");

        RouteOwnershipRules.IsObsoleteOwnRoute(ruta, MyBackends, DesiredEmpty).Should().BeTrue();
    }

    // ---- Casos adicionales que fijan el resto del contrato ----

    [Theory]
    [InlineData(null)]               // pre-existe al campo Origin
    [InlineData("backfill_backend")] // migración de datos, no el reconciliador
    [InlineData("backfill_hostname")]
    [InlineData("manual")]
    public void Origin_nulo_o_ajeno_nunca_se_trata_como_mio(string? origin)
    {
        var ruta = Route("cualquier-host.authoritforge.dev", "http://factusforge-api:8080", origin);

        RouteOwnershipRules.IsObsoleteOwnRoute(ruta, MyBackends, DesiredEmpty).Should().BeFalse();
    }

    [Fact]
    public void Una_ruta_propia_vigente_bajo_el_host_deseado_no_se_toca()
    {
        var deseado = new HashSet<(string Host, string Prefix)> { ("factusforge-app.authoritforge.dev", "/") };
        var ruta = Route("factusforge-app.authoritforge.dev", "http://factusforge-api:8080", origin: "native_deploy");

        RouteOwnershipRules.IsObsoleteOwnRoute(ruta, MyBackends, deseado).Should().BeFalse();
    }

    [Fact]
    public void Una_ruta_con_Origin_propio_pero_backend_distinto_no_se_toca()
    {
        // Origin="native_deploy" de OTRA Instance (mismo marcador, backend distinto) — el backend
        // sigue siendo la primera guarda, Origin por sí solo no basta para reclamarla.
        var ruta = Route("otra-instance.authoritforge.dev", "http://otra-instance-api:8080", origin: "native_deploy");

        RouteOwnershipRules.IsObsoleteOwnRoute(ruta, MyBackends, DesiredEmpty).Should().BeFalse();
    }

    [Fact]
    public void El_match_de_host_deseado_es_case_insensitive_pero_de_path_exacto()
    {
        var deseado = new HashSet<(string Host, string Prefix)> { ("factusforge-app.authoritforge.dev", "/") };
        var ruta = Route("FactusForge-App.authoritforge.dev", "http://factusforge-api:8080", origin: "native_deploy");

        RouteOwnershipRules.IsObsoleteOwnRoute(ruta, MyBackends, deseado).Should().BeFalse();
    }
}
