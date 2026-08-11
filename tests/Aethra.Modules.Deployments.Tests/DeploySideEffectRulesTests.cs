using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// OT-006 <c>#52</c>/<c>#53</c> — un fallo que no se propaga hace que el rollout no pueda saber que
/// falló. El runner etiquetaba CUALQUIER error de <c>CreateRouteCommand</c> como "(ya existía)" y ni
/// siquiera asignaba el resultado de <c>CreateMonitorCommand</c>.
/// </summary>
public sealed class DeploySideEffectRulesTests
{
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
        => DeploySideEffectRules.ClassifyRoute(true, null).Should().Be(SideEffectOutcome.Created);

    /// <summary>
    /// El caso normal de un redeploy: la ruta ya está y el comando devuelve conflicto. Eso NO es un
    /// fallo, y por eso el bug original resultaba invisible.
    /// </summary>
    [Fact]
    public void The_hostname_conflict_is_the_only_benign_route_failure()
        => DeploySideEffectRules.ClassifyRoute(false, DeploySideEffectRules.RouteAlreadyExistsCode)
            .Should().Be(SideEffectOutcome.AlreadyExists);

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
        => DeploySideEffectRules.ClassifyRoute(false, errorCode).Should().Be(SideEffectOutcome.Failed);

    /// <summary>
    /// La lista de códigos benignos es CERRADA: un fallo sin código, o con uno que no conocemos, se
    /// rompe hacia el lado ruidoso. Un código nuevo nunca puede volverse silencioso por defecto.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("algo.que.nadie.ha.visto")]
    public void An_unclassified_route_failure_is_never_already_exists(string? errorCode)
        => DeploySideEffectRules.ClassifyRoute(false, errorCode).Should().Be(SideEffectOutcome.Failed);

    [Fact]
    public void Route_code_comparison_is_case_sensitive()
        => DeploySideEffectRules.ClassifyRoute(false, "Route.Hostname_Taken")
            .Should().Be(SideEffectOutcome.Failed);

    [Fact]
    public void A_created_monitor_is_reported_as_created()
        => DeploySideEffectRules.ClassifyMonitor(true, null).Should().Be(SideEffectOutcome.Created);

    /// <summary>Los dos conflictos que un redeploy produce siempre: el monitor ya está.</summary>
    [Theory]
    [InlineData("monitor.slug_taken")]
    [InlineData("monitor.url_taken")]
    public void The_two_monitor_conflicts_are_the_normal_redeploy(string errorCode)
        => DeploySideEffectRules.ClassifyMonitor(false, errorCode).Should().Be(SideEffectOutcome.AlreadyExists);

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
        => DeploySideEffectRules.ClassifyMonitor(false, errorCode).Should().Be(SideEffectOutcome.Failed);

    [Fact]
    public void A_route_conflict_code_does_not_silence_a_monitor_failure_and_viceversa()
    {
        DeploySideEffectRules.ClassifyMonitor(false, DeploySideEffectRules.RouteAlreadyExistsCode)
            .Should().Be(SideEffectOutcome.Failed);
        DeploySideEffectRules.ClassifyRoute(false, DeploySideEffectRules.MonitorSlugTakenCode)
            .Should().Be(SideEffectOutcome.Failed);
    }
}
