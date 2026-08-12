using Aethra.Modules.Services.Infrastructure.Provisioning;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

public sealed class PostgresRevokeRulesTests
{
    [Fact]
    public void Revoke_plan_targets_the_persisted_binding_username_not_the_resource_name()
    {
        var plan = PostgresRevokePlan.Create(
            "appdb",
            new BindingCredentials("appdb_user", "secret"),
            new AdminCredentials("postgres", "admin-secret"));

        plan.DatabaseIdentifier.Should().Be("\"appdb\"");
        plan.UserIdentifier.Should().Be("\"appdb_user\"");
        plan.UserIdentifier.Should().NotBe(plan.DatabaseIdentifier);
    }

    [Fact]
    public void Classify_treats_only_absent_role_or_absent_database_cases_as_idempotent()
    {
        var cases = new[]
        {
            (PostgresRevokeStep.RevokeDatabasePrivileges, PostgresRevokeRules.UndefinedObject),
            (PostgresRevokeStep.RevokeDatabasePrivileges, PostgresRevokeRules.InvalidCatalogName),
            (PostgresRevokeStep.RestoreDatabaseOwner, PostgresRevokeRules.InvalidCatalogName),
            (PostgresRevokeStep.OpenTargetDatabase, PostgresRevokeRules.InvalidCatalogName),
            (PostgresRevokeStep.ReassignOwnedObjects, PostgresRevokeRules.UndefinedObject),
            (PostgresRevokeStep.DropOwnedObjects, PostgresRevokeRules.UndefinedObject),
            (PostgresRevokeStep.DropRole, PostgresRevokeRules.UndefinedObject),
        };

        foreach (var (step, sqlState) in cases)
        {
            PostgresRevokeRules.Classify(step, sqlState)
                .Should().Be(PostgresRevokeErrorDecision.BenignIdempotent);
        }
    }

    [Theory]
    [InlineData("42501")] // insufficient_privilege
    [InlineData("2BP01")] // dependent_objects_still_exist
    [InlineData("53300")] // too_many_connections
    [InlineData("XX000")] // internal_error
    public void Classify_keeps_permission_dependency_and_unexpected_failures_fatal(string sqlState)
    {
        foreach (var step in Enum.GetValues<PostgresRevokeStep>())
        {
            PostgresRevokeRules.Classify(step, sqlState)
                .Should().Be(PostgresRevokeErrorDecision.Fatal);
        }
    }

    /// <summary>
    /// Un servicio ADOPTADO trae un admin que eligió otro: puede llevar '-' o '@', que Postgres
    /// acepta entre comillas pero el alfabeto de los identificadores que generamos nosotros no.
    /// Citarlo con esa allowlist abortaba la revocación antes de tocar la base, así que ese servicio
    /// podía provisionar bindings y no revocarlos <b>nunca</b>: la credencial quedaba activa para
    /// siempre. Se reasigna la propiedad a CURRENT_USER, que es el mismo rol con el que ya está
    /// autenticada la sesión, así que no hay nombre que citar.
    /// </summary>
    [Theory]
    [InlineData("admin-user")]
    [InlineData("svc@tenant")]
    [InlineData("Admin.With.Dots")]
    public void An_adopted_admin_name_never_blocks_the_plan(string adminUsername)
    {
        var plan = PostgresRevokePlan.Create(
            "app_db",
            new BindingCredentials("app_db_user", "secret"),
            new AdminCredentials(adminUsername, "secret"));

        plan.AdminIdentifier.Should().Be("CURRENT_USER");
        plan.AdminUsername.Should().Be(adminUsername);
    }
}
