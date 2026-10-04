using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// OT-069 F2 — fija la barrida post-deploy de contenedores nativos. El bug real mató
/// <c>yunke-redis</c> porque compartía el prefijo del slug pero era un servicio gestionado, no un
/// zombi de la app.
/// </summary>
public sealed class ContainerCleanupRulesTests
{
    private const string Slug = "yunke";

    [Fact]
    public void A_managed_service_container_with_the_instance_prefix_survives_cleanup()
    {
        var stale = Select(
            containers: ["yunke-api", "yunke-redis"],
            templateServices: ["api"],
            managedServiceContainers: ["yunke-redis"]);

        stale.Should().BeEmpty();
    }

    [Fact]
    public void An_app_zombie_with_a_rollout_suffix_is_still_removed()
    {
        var stale = Select(
            containers: ["yunke-api", "yunke-api-bgbak", "yunke-redis"],
            templateServices: ["api"],
            managedServiceContainers: ["yunke-redis"]);

        stale.Should().Equal("yunke-api-bgbak");
    }

    [Fact]
    public void A_service_removed_from_the_template_is_still_removed()
    {
        var stale = Select(
            containers: ["yunke-api", "yunke-worker", "yunke-redis"],
            templateServices: ["api"],
            managedServiceContainers: ["yunke-redis"]);

        stale.Should().Equal("yunke-worker");
    }

    [Fact]
    public void A_container_from_another_slug_is_never_selected()
    {
        var stale = Select(
            containers: ["yunke-api", "yunke2-api-bgbak", "yunke-cache"],
            templateServices: ["api"],
            managedServiceContainers: []);

        stale.Should().Equal("yunke-cache");
    }

    private static IReadOnlyList<string> Select(
        IReadOnlyList<string> containers,
        IReadOnlyList<string> templateServices,
        IReadOnlyList<string> managedServiceContainers)
        => ContainerCleanupRules.SelectStaleContainerNames(
            containers,
            Slug,
            templateServices,
            managedServiceContainers);
}
