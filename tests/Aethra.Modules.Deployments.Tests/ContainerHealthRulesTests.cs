using Aethra.Modules.Deployments.Rollout;
using Aethra.Shared.Contracts.Containers;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// OT-006 <c>#51</c> — la regla que decide si el reemplazo de un rollout nativo está SANO.
/// El runner decidía con <c>Status.StartsWith("Up")</c>, que es el "arrancó" de Docker y NO el
/// healthcheck: un contenedor con el healthcheck fallando sustituía a la revisión anterior.
/// Los estados de ejemplo salen de producción (<c>docker ps</c> en la VM de Aethra, 2026-08-11).
/// </summary>
public sealed class ContainerHealthRulesTests
{
    private static ContainerInfo Container(string name, string status)
        => new($"id-{name}", name, $"aethra/{name}:sha", status, []);

    [Theory]
    // Sin HEALTHCHECK declarado en la imagen: es el caso de TODOS los contenedores nativos de hoy
    // (factusforge-*, ekippo-*, relaycore-*, paradoxbox-*) → siguen pasando igual que antes.
    [InlineData("Up 38 hours", ContainerHealthState.Healthy)]
    [InlineData("Up 2 days", ContainerHealthState.Healthy)]
    [InlineData("Up About a minute", ContainerHealthState.Healthy)]
    // Con HEALTHCHECK declarado (en prod hoy: openclaw-gateway).
    [InlineData("Up 3 days (healthy)", ContainerHealthState.Healthy)]
    [InlineData("Up 2 minutes (unhealthy)", ContainerHealthState.Unhealthy)]
    [InlineData("Up 3 seconds (health: starting)", ContainerHealthState.Starting)]
    // No está sirviendo.
    [InlineData("Restarting (1) 2 seconds ago", ContainerHealthState.NotRunning)]
    [InlineData("Exited (137) 5 seconds ago", ContainerHealthState.NotRunning)]
    [InlineData("Exited (0) 2 minutes ago", ContainerHealthState.NotRunning)]
    [InlineData("Created", ContainerHealthState.NotRunning)]
    [InlineData("Dead", ContainerHealthState.NotRunning)]
    [InlineData("Up 3 days (Paused)", ContainerHealthState.NotRunning)]
    // Fallback del mapeo de Podman (Status ?? State).
    [InlineData("running", ContainerHealthState.Healthy)]
    // Sin dato = no verificado, nunca "sano".
    [InlineData("", ContainerHealthState.Absent)]
    [InlineData("   ", ContainerHealthState.Absent)]
    [InlineData(null, ContainerHealthState.Absent)]
    public void Evaluate_reads_the_declared_healthcheck_and_not_only_the_Up_prefix(
        string? status, ContainerHealthState expected)
        => ContainerHealthRules.Evaluate(status).Should().Be(expected);

    /// <summary>
    /// El corazón de <c>#51</c>: estos tres estados EMPIEZAN por "Up" —o sea, el predicado viejo los
    /// daba por sanos— y ninguno lo está. Es la regresión que este test cierra.
    /// </summary>
    [Theory]
    [InlineData("Up 2 minutes (unhealthy)")]
    [InlineData("Up 3 seconds (health: starting)")]
    [InlineData("Up 4 seconds (Paused)")]
    public void An_Up_container_that_is_not_healthy_is_treated_as_failure(string deceivingStatus)
    {
        // El predicado que usaba NativeDeployRunner antes del fix.
        deceivingStatus.StartsWith("Up", StringComparison.OrdinalIgnoreCase).Should().BeTrue();

        ContainerHealthRules.Evaluate(deceivingStatus).Should().NotBe(ContainerHealthState.Healthy);

        var verdict = ContainerHealthRules.EvaluateAll(
            ["yunke-api"], [Container("yunke-api", deceivingStatus)]);
        verdict.AllHealthy.Should().BeFalse();
        verdict.Blockers.Should().ContainSingle();
        verdict.Blockers[0].Should().Contain("yunke-api").And.Contain(deceivingStatus);
    }

    [Fact]
    public void Healthy_rollout_needs_every_target_container_up()
    {
        var verdict = ContainerHealthRules.EvaluateAll(
            ["yunke-api", "yunke-admin", "yunke-tenant"],
            [
                Container("yunke-api", "Up 10 seconds"),
                Container("yunke-admin", "Up 9 seconds"),
                Container("yunke-tenant", "Up 8 seconds"),
            ]);

        verdict.AllHealthy.Should().BeTrue();
        verdict.Blockers.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_container_blocks_the_rollout_and_says_which_one()
    {
        var verdict = ContainerHealthRules.EvaluateAll(
            ["yunke-api", "yunke-admin"],
            [Container("yunke-api", "Up 10 seconds")]);

        verdict.AllHealthy.Should().BeFalse();
        verdict.Blockers.Should().ContainSingle();
        verdict.Blockers[0].Should().Contain("yunke-admin");
    }

    /// <summary>
    /// Un <c>All()</c> sobre una colección vacía es verdadero por vacuidad. Aquí eso sería declarar
    /// sano un rollout que nadie verificó — el falso verde exacto que esta OT persigue.
    /// </summary>
    [Fact]
    public void No_target_containers_is_never_healthy()
    {
        var verdict = ContainerHealthRules.EvaluateAll([], []);

        verdict.AllHealthy.Should().BeFalse();
        verdict.Blockers.Should().NotBeEmpty();
    }

    [Fact]
    public void Container_names_are_matched_ordinally()
        => ContainerHealthRules.EvaluateService("yunke-api", [Container("Yunke-API", "Up 1 hour")])
            .Should().Be(ContainerHealthState.Absent);

    [Fact]
    public void A_container_of_another_instance_does_not_satisfy_the_healthcheck()
    {
        var verdict = ContainerHealthRules.EvaluateAll(
            ["yunke-api"], [Container("ekippo-backend", "Up 3 days")]);

        verdict.AllHealthy.Should().BeFalse();
    }
}
