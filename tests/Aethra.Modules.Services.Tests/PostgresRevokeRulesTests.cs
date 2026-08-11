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
}
