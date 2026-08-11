using Aethra.Modules.Deployments.Rollout;
using Aethra.Shared.Contracts.Containers;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// OT-006 <c>#49</c>/<c>#50</c> — qué se restaura cuando un rollout nativo falla, y en qué orden.
///
/// <para><c>#49</c>: el runner borraba el contenedor previo con <c>force:true</c> ANTES de levantar
/// el reemplazo; si el nuevo no arrancaba o no quedaba sano, el servicio se quedaba sin nada
/// corriendo. <c>#50</c>: en un despliegue multi-servicio, el <c>foreach</c> retornaba <c>Fail</c>
/// dejando los servicios ya sustituidos a medias y sin recuperación.</para>
///
/// <para>El escenario de los tests es el de producción: la Instance <c>factusforge</c> (Yunke)
/// despliega 4 servicios con nombres estables <c>{slug}-{servicio}</c> e imágenes
/// <c>aethra/{slug}-{servicio}:{sha}</c>.</para>
/// </summary>
public sealed class NativeRolloutPlannerTests
{
    private static ContainerInfo Running(string name, string image)
        => new($"id-{name}", name, image, "Up 3 days", []);

    private static readonly ContainerInfo[] ProdBefore =
    [
        Running("factusforge-api", "aethra/factusforge-api:d7e38aa"),
        Running("factusforge-admin", "aethra/factusforge-admin:d7e38aa"),
        Running("factusforge-tenant", "aethra/factusforge-tenant:d7e38aa"),
        Running("factusforge-landing", "aethra/factusforge-landing:d7e38aa"),
    ];

    private static ServiceReplacement Replace(string service, string newSha)
        => NativeRolloutPlanner.Capture(
            service, $"factusforge-{service}", $"aethra/factusforge-{service}:{newSha}", ProdBefore);

    [Fact]
    public void Capture_takes_the_previous_image_and_id_from_the_snapshot()
    {
        var r = Replace("api", "beefcaf");

        r.ServiceName.Should().Be("api");
        r.ContainerName.Should().Be("factusforge-api");
        r.NewImageRef.Should().Be("aethra/factusforge-api:beefcaf");
        r.PreviousImageRef.Should().Be("aethra/factusforge-api:d7e38aa");
        r.PreviousContainerId.Should().Be("id-factusforge-api");
    }

    [Fact]
    public void Capture_with_no_previous_container_has_nothing_to_restore()
    {
        var r = NativeRolloutPlanner.Capture("api", "nuevo-api", "aethra/nuevo-api:1", ProdBefore);

        r.PreviousImageRef.Should().BeNull();
        r.PreviousContainerId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_previous_container_without_a_readable_image_is_not_restorable(string image)
    {
        var r = NativeRolloutPlanner.Capture(
            "api", "factusforge-api", "aethra/factusforge-api:nuevo",
            [Running("factusforge-api", image)]);

        r.PreviousImageRef.Should().BeNull();
        NativeRolloutPlanner.PlanRollback([r])[0].Action.Should().Be(RollbackAction.LeaveForDiagnosis);
    }

    /// <summary>
    /// Criterio de aceptación 1 de la OT: un reemplazo que no pasa el healthcheck deja vivo al
    /// contenedor anterior — es decir, el plan de deshacer restaura EXACTAMENTE la imagen previa.
    /// </summary>
    [Fact]
    public void A_replacement_that_fails_leaves_the_previous_revision_running()
    {
        var replaced = Replace("api", "roto123");

        var plan = NativeRolloutPlanner.PlanRollback([replaced]);

        plan.Should().ContainSingle();
        plan[0].Action.Should().Be(RollbackAction.RestorePrevious);
        plan[0].ServiceName.Should().Be("api");
        plan[0].ContainerName.Should().Be("factusforge-api");
        plan[0].RestoreImageRef.Should().Be("aethra/factusforge-api:d7e38aa");
        plan[0].RestoreImageRef.Should().NotBe(replaced.NewImageRef);
    }

    /// <summary>
    /// Criterio de aceptación 2 de la OT: un despliegue de N servicios que falla en el servicio k
    /// restaura los ya sustituidos. El servicio k entra en la lista ANTES de su propio <c>remove</c>
    /// (a partir de ahí ya se quedó sin contenedor), así que también se restaura; el k+1 y
    /// siguientes, que nunca se tocaron, NO aparecen en el plan.
    /// </summary>
    [Fact]
    public void A_multi_service_rollout_failing_on_service_k_restores_every_service_already_replaced()
    {
        // 4 servicios; falla el 3º (tenant). El 4º (landing) nunca se tocó.
        var replacements = new[] { Replace("api", "nuevo"), Replace("admin", "nuevo"), Replace("tenant", "nuevo") };

        var plan = NativeRolloutPlanner.PlanRollback(replacements);

        plan.Should().HaveCount(3);
        plan.Select(s => s.ServiceName).Should().NotContain("landing");
        plan.Should().OnlyContain(s => s.Action == RollbackAction.RestorePrevious);
        plan.Select(s => s.RestoreImageRef).Should().AllSatisfy(i => i.Should().EndWith(":d7e38aa"));
    }

    [Fact]
    public void The_plan_unwinds_in_reverse_order_of_application()
    {
        var replacements = new[] { Replace("api", "nuevo"), Replace("admin", "nuevo"), Replace("tenant", "nuevo") };

        var plan = NativeRolloutPlanner.PlanRollback(replacements);

        plan.Select(s => s.ServiceName).Should().ContainInOrder("tenant", "admin", "api");
    }

    /// <summary>
    /// Primer deploy de un servicio: no hay revisión previa que restaurar. El contenedor nuevo se
    /// deja en su sitio a propósito (sus logs son el único diagnóstico y no hay tráfico anterior
    /// que proteger), pero el paso queda explícito en el plan, no implícito por omisión.
    /// </summary>
    [Fact]
    public void A_first_time_service_is_left_for_diagnosis_instead_of_being_restored()
    {
        var replaced = NativeRolloutPlanner.Capture("nuevo", "factusforge-nuevo", "aethra/factusforge-nuevo:1", ProdBefore);

        var plan = NativeRolloutPlanner.PlanRollback([replaced]);

        plan.Should().ContainSingle();
        plan[0].Action.Should().Be(RollbackAction.LeaveForDiagnosis);
        plan[0].RestoreImageRef.Should().BeNull();
    }

    [Fact]
    public void A_mixed_rollout_restores_what_it_can_and_marks_the_rest()
    {
        var replacements = new[]
        {
            Replace("api", "nuevo"),
            NativeRolloutPlanner.Capture("nuevo", "factusforge-nuevo", "aethra/factusforge-nuevo:1", ProdBefore),
        };

        var plan = NativeRolloutPlanner.PlanRollback(replacements);

        plan.Should().HaveCount(2);
        plan[0].Action.Should().Be(RollbackAction.LeaveForDiagnosis); // el último aplicado, sin previo
        plan[1].Action.Should().Be(RollbackAction.RestorePrevious);   // api vuelve a d7e38aa
        plan[1].RestoreImageRef.Should().Be("aethra/factusforge-api:d7e38aa");
    }

    [Fact]
    public void Nothing_replaced_yet_means_nothing_to_undo()
        => NativeRolloutPlanner.PlanRollback([]).Should().BeEmpty();

    /// <summary>
    /// Redeploy del mismo sha (o de un tag móvil como <c>:latest</c>): "restaurar" es re-levantar la
    /// misma imagen. Sigue siendo lo correcto — devuelve el servicio al estado que tenía — y no debe
    /// confundirse con "no hay nada que restaurar".
    /// </summary>
    [Fact]
    public void Redeploying_the_same_image_still_produces_a_restore_step()
    {
        var replaced = NativeRolloutPlanner.Capture(
            "api", "factusforge-api", "aethra/factusforge-api:d7e38aa", ProdBefore);

        var plan = NativeRolloutPlanner.PlanRollback([replaced]);

        plan[0].Action.Should().Be(RollbackAction.RestorePrevious);
        plan[0].RestoreImageRef.Should().Be("aethra/factusforge-api:d7e38aa");
    }

    [Fact]
    public void Container_names_are_matched_ordinally_when_capturing()
    {
        var r = NativeRolloutPlanner.Capture(
            "api", "FACTUSFORGE-API", "aethra/factusforge-api:nuevo", ProdBefore);

        r.PreviousImageRef.Should().BeNull();
    }
}
