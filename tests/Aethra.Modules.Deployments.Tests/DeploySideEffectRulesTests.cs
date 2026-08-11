using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// OT-006 <c>#52</c>/<c>#53</c> — un fallo que no se propaga hace que el rollout no pueda saber que
/// falló. El runner etiquetaba CUALQUIER error de <c>CreateRouteCommand</c> como "(ya existía)" y ni
/// siquiera asignaba el resultado de <c>CreateMonitorCommand</c>.
///
/// <para>G2 de OT-006 (hallazgos <c>B4</c>/<c>B5</c>) — y el código de error TAMPOCO basta. Los
/// handlers devuelven su conflicto mirando solo la CLAVE (host+path para la ruta, slug para el
/// monitor), sin comparar a dónde apunta lo que ya existe. Dar eso por benigno es reportar un deploy
/// exitoso mientras el host sirve otra aplicación. Estos tests fijan la regla nueva: "ya existía"
/// solo es benigno si lo que ya está es MÍO.</para>
/// </summary>
public sealed class DeploySideEffectRulesTests
{
    private const string MyBackend = "http://miapp-web:8080";
    private const string OtherBackend = "http://otraapp-web:8080";
    private const string MyUrl = "https://miapp.example.com/";
    private const string OtherUrl = "https://otraapp.example.com/";

    /// <summary>
    /// Guarda de acoplamiento: estos literales deben coincidir EXACTO con los que devuelven los
    /// handlers — <c>CreateRouteHandler</c> (<c>Error.Conflict("route.hostname_taken", …)</c>) y
    /// <c>CreateMonitorHandler</c> (<c>"monitor.slug_taken"</c> / <c>"monitor.url_taken"</c>).
    /// El módulo Deployments no referencia Proxy ni Monitoring (aislamiento declarado en su csproj),
    /// así que el vínculo se fija aquí: si alguien renombra un código allá, este test lo caza.
    /// </summary>
    [Fact]
    public void Benign_codes_match_the_handlers_literals()
    {
        DeploySideEffectRules.RouteAlreadyExistsCode.Should().Be("route.hostname_taken");
        DeploySideEffectRules.MonitorSlugTakenCode.Should().Be("monitor.slug_taken");
        DeploySideEffectRules.MonitorUrlTakenCode.Should().Be("monitor.url_taken");
    }

    [Fact]
    public void A_created_route_is_reported_as_created()
        => DeploySideEffectRules.ClassifyRoute(true, null, MyBackend, null)
            .Should().Be(SideEffectOutcome.Created);

    /// <summary>
    /// El caso normal de un redeploy: la ruta ya está, apuntando a mi propio backend. Eso NO es un
    /// fallo, y por eso el bug original resultaba invisible.
    /// </summary>
    [Fact]
    public void A_conflict_against_my_own_backend_is_the_normal_redeploy()
        => DeploySideEffectRules.ClassifyRoute(
                false, DeploySideEffectRules.RouteAlreadyExistsCode, MyBackend, MyBackend)
            .Should().Be(SideEffectOutcome.AlreadyExists);

    /// <summary>
    /// <b>El caso que da nombre al hallazgo B4.</b> La ruta existe pero sirve OTRO backend: el
    /// handler devuelve igualmente <c>route.hostname_taken</c> y no actualiza la fila. Si esto se
    /// clasificara benigno, el deploy diría "OK (ya existía)" mientras el tráfico del cliente va a
    /// una aplicación ajena — secuestro silencioso, con el operador viendo verde.
    /// </summary>
    [Fact]
    public void A_conflict_against_someone_elses_backend_is_a_failure()
        => DeploySideEffectRules.ClassifyRoute(
                false, DeploySideEffectRules.RouteAlreadyExistsCode, MyBackend, OtherBackend)
            .Should().Be(SideEffectOutcome.Failed);

    /// <summary>
    /// Falla cerrado: si no se pudo averiguar a dónde apunta la ruta existente (el listado falló, o
    /// la ruta apareció después de la foto), NO se puede afirmar que sea mía. No poder demostrar la
    /// propiedad no es lo mismo que demostrarla.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unknown_existing_backend_fails_closed(string? existingBackend)
        => DeploySideEffectRules.ClassifyRoute(
                false, DeploySideEffectRules.RouteAlreadyExistsCode, MyBackend, existingBackend)
            .Should().Be(SideEffectOutcome.Failed);

    /// <summary>
    /// La comparación tolera lo que NO cambia el destino: espacios, barra final y caja (esquema y
    /// host son case-insensitive por RFC 3986). Si no, cada redeploy sería una falsa alarma.
    /// </summary>
    [Theory]
    [InlineData("http://miapp-web:8080/")]
    [InlineData("  http://miapp-web:8080  ")]
    [InlineData("HTTP://MiApp-Web:8080")]
    public void Cosmetic_differences_in_the_backend_are_still_mine(string existingBackend)
        => DeploySideEffectRules.ClassifyRoute(
                false, DeploySideEffectRules.RouteAlreadyExistsCode, MyBackend, existingBackend)
            .Should().Be(SideEffectOutcome.AlreadyExists);

    /// <summary>
    /// El puerto SÍ cuenta: <c>:8080</c> y <c>:9090</c> son destinos distintos, aunque el host
    /// coincida. Un contenedor sirviendo en otro puerto es otra cosa.
    /// </summary>
    [Fact]
    public void A_different_port_is_a_different_backend()
        => DeploySideEffectRules.ClassifyRoute(
                false, DeploySideEffectRules.RouteAlreadyExistsCode, MyBackend, "http://miapp-web:9090")
            .Should().Be(SideEffectOutcome.Failed);

    /// <summary>
    /// Criterio de aceptación 4 de la OT: un <c>CreateRouteCommand</c> que falla por una causa
    /// distinta de "ya existía" propaga el fallo. Los códigos son los reales que puede devolver ese
    /// camino: backend inválido, hostname inválido y el corto-circuito del ValidationBehavior.
    /// </summary>
    [Theory]
    [InlineData("route.invalid_backend")]
    [InlineData("hostname.invalid")]
    [InlineData("validation.failed")]
    [InlineData("route.hostname_taken_but_not_really")]
    public void A_route_failure_other_than_already_exists_is_propagated(string errorCode)
        => DeploySideEffectRules.ClassifyRoute(false, errorCode, MyBackend, MyBackend)
            .Should().Be(SideEffectOutcome.Failed);

    /// <summary>
    /// La lista de códigos benignos es CERRADA: un fallo sin código, o con uno que no conocemos, se
    /// rompe hacia el lado ruidoso. Un código nuevo nunca puede volverse silencioso por defecto.
    /// Ni siquiera coincidiendo el backend, porque el fallo no fue un conflicto de ruta.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("algo.que.nadie.ha.visto")]
    public void An_unclassified_route_failure_is_never_already_exists(string? errorCode)
        => DeploySideEffectRules.ClassifyRoute(false, errorCode, MyBackend, MyBackend)
            .Should().Be(SideEffectOutcome.Failed);

    [Fact]
    public void Route_code_comparison_is_case_sensitive()
        => DeploySideEffectRules.ClassifyRoute(false, "Route.Hostname_Taken", MyBackend, MyBackend)
            .Should().Be(SideEffectOutcome.Failed);

    [Fact]
    public void A_created_monitor_is_reported_as_created()
        => DeploySideEffectRules.ClassifyMonitor(true, null, MyUrl, null)
            .Should().Be(SideEffectOutcome.Created);

    /// <summary>
    /// <c>url_taken</c> es benigno sin más: su guarda compara la URL normalizada, así que el propio
    /// código ya prueba que ESA url está vigilada. No hace falta saber nada más.
    /// </summary>
    [Fact]
    public void The_url_conflict_proves_the_app_is_watched()
        => DeploySideEffectRules.ClassifyMonitor(
                false, DeploySideEffectRules.MonitorUrlTakenCode, MyUrl, null)
            .Should().Be(SideEffectOutcome.AlreadyExists);

    /// <summary>
    /// <c>slug_taken</c> con el monitor apuntando a MI host: redeploy normal, la app está vigilada.
    /// </summary>
    [Fact]
    public void A_slug_conflict_on_my_own_host_is_the_normal_redeploy()
        => DeploySideEffectRules.ClassifyMonitor(
                false, DeploySideEffectRules.MonitorSlugTakenCode, MyUrl, MyUrl)
            .Should().Be(SideEffectOutcome.AlreadyExists);

    /// <summary>
    /// El grano es el HOST, no la URL exacta, y es deliberado: un operador puede haber apuntado el
    /// monitor a un endpoint mejor que la raíz. Caso real en producción: el monitor de <c>ekippo</c>
    /// vigila <c>/login</c> en vez de <c>/</c>. Esa app SÍ está vigilada; exigir igualdad exacta
    /// convertiría cada redeploy en una falsa alarma.
    /// </summary>
    [Fact]
    public void A_monitor_watching_a_better_endpoint_of_my_host_still_counts()
        => DeploySideEffectRules.ClassifyMonitor(
                false, DeploySideEffectRules.MonitorSlugTakenCode, MyUrl, "https://miapp.example.com/login")
            .Should().Be(SideEffectOutcome.AlreadyExists);

    /// <summary>
    /// <b>Hallazgo B5.</b> El slug lo tiene el monitor de OTRA aplicación: la mía se queda sin
    /// vigilancia y, clasificado como benigno, nadie se entera. Es justo lo que <c>#53</c> cerraba.
    /// </summary>
    [Fact]
    public void A_slug_taken_by_another_apps_monitor_is_a_failure()
        => DeploySideEffectRules.ClassifyMonitor(
                false, DeploySideEffectRules.MonitorSlugTakenCode, MyUrl, OtherUrl)
            .Should().Be(SideEffectOutcome.Failed);

    /// <summary>
    /// Falla cerrado también aquí: sin saber qué URL vigila el slug ocupado, o con una URL que no
    /// es absoluta ni parseable, no se puede afirmar que mi app esté vigilada.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/login")]
    [InlineData("no-es-una-url")]
    public void An_unknown_or_unparseable_monitor_url_fails_closed(string? existingUrl)
        => DeploySideEffectRules.ClassifyMonitor(
                false, DeploySideEffectRules.MonitorSlugTakenCode, MyUrl, existingUrl)
            .Should().Be(SideEffectOutcome.Failed);

    /// <summary>
    /// Criterio de aceptación 5 de la OT: un <c>CreateMonitorCommand</c> fallido queda visible. Los
    /// códigos son los reales del handler para configuración inválida.
    /// </summary>
    [Theory]
    [InlineData("monitor.invalid_method")]
    [InlineData("monitor.invalid_config")]
    [InlineData("slug.invalid")]
    [InlineData("validation.failed")]
    [InlineData(null)]
    [InlineData("")]
    public void A_monitor_that_could_not_be_created_is_a_visible_failure(string? errorCode)
        => DeploySideEffectRules.ClassifyMonitor(false, errorCode, MyUrl, MyUrl)
            .Should().Be(SideEffectOutcome.Failed);

    [Fact]
    public void A_route_conflict_code_does_not_silence_a_monitor_failure_and_viceversa()
    {
        DeploySideEffectRules.ClassifyMonitor(
                false, DeploySideEffectRules.RouteAlreadyExistsCode, MyUrl, MyUrl)
            .Should().Be(SideEffectOutcome.Failed);
        DeploySideEffectRules.ClassifyRoute(
                false, DeploySideEffectRules.MonitorSlugTakenCode, MyBackend, MyBackend)
            .Should().Be(SideEffectOutcome.Failed);
    }
}
