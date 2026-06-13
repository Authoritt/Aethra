using Aethra.Modules.Identity.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// <see cref="PasswordHasher"/> (Argon2id con salt aleatorio embebido) — roundtrip hash/verify,
/// rechazo de password incorrecto y de hash mal formado. Y los factories de <see cref="Role"/>
/// (custom vs system, validación de scopes contra el catálogo, slugs de sistema).
/// </summary>
public sealed class IdentityHashingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] RoleScopes = ["vms:read", "services:write"];

    // ---------- PasswordHasher ----------

    [Fact]
    public void PasswordHasher_hash_then_verify_roundtrips()
    {
        var hash = PasswordHasher.Hash("S3cr3t-Pass!");

        PasswordHasher.Verify("S3cr3t-Pass!", hash).Should().BeTrue();
    }

    [Fact]
    public void PasswordHasher_verify_rejects_a_wrong_password()
    {
        var hash = PasswordHasher.Hash("correct-horse");

        PasswordHasher.Verify("wrong-horse", hash).Should().BeFalse();
    }

    [Fact]
    public void PasswordHasher_uses_a_random_salt_so_two_hashes_of_the_same_password_differ()
        => PasswordHasher.Hash("same-password").Should().NotBe(PasswordHasher.Hash("same-password"));

    [Fact]
    public void PasswordHasher_verify_rejects_a_malformed_hash()
        => PasswordHasher.Verify("anything", "not-a-valid-argon2-hash").Should().BeFalse();

    // ---------- Role ----------

    [Fact]
    public void Role_CreateCustom_lowercases_slug_trims_name_and_is_not_system()
    {
        var role = Role.CreateCustom("  Ops-Team ", "  Ops Team  ", RoleScopes, Now);

        role.Slug.Should().Be("ops-team");
        role.DisplayName.Should().Be("Ops Team");
        role.IsSystem.Should().BeFalse();
        role.Scopes.Should().Contain("vms:read");
    }

    [Fact]
    public void Role_CreateSystem_marks_is_system_true()
        => Role.CreateSystem("admin", "Admin", RoleScopes, Now).IsSystem.Should().BeTrue();

    [Fact]
    public void Role_CreateCustom_requires_at_least_one_scope()
    {
        var act = () => Role.CreateCustom("ops", "Ops", Array.Empty<string>(), Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Role_CreateCustom_rejects_a_scope_outside_the_catalog()
    {
        var bad = new[] { "not-a-real-scope" };

        var act = () => Role.CreateCustom("ops", "Ops", bad, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Role_SystemSlugs_are_the_three_builtins()
    {
        Role.SystemSlugs.Should().HaveCount(3);
        Role.SystemSlugs.Should().Contain(Role.AdminSlug);
    }
}
