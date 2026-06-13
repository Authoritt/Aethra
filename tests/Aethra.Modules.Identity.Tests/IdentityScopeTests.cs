using Aethra.Modules.Identity.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// La decisión de autorización <c>HasScope</c> de ApiKey y Role: match exacto o el wildcard
/// global <c>*</c> (admin). NO hay matching jerárquico/prefijo, y un scope en blanco SIEMPRE
/// es false (incluso con admin). Seguridad-crítico — gobierna el acceso a cada endpoint.
/// Nota: ApiKey.Create valida los scopes contra <see cref="ApiKey.AllScopes"/> (Role no), así que
/// se usan nombres reales del catálogo (services:read, vms:write, ...).
/// </summary>
public sealed class IdentityScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ---------- ApiKey.HasScope ----------

    private static ApiKey NewKey(params string[] scopes)
        => ApiKey.Create("ci", "secret-plaintext", scopes, Now, new ApiKeyHasher());

    [Fact]
    public void ApiKey_HasScope_matches_an_exact_scope()
    {
        var key = NewKey("services:read", "services:write");

        key.HasScope("services:read").Should().BeTrue();
        key.HasScope("services:write").Should().BeTrue();
        key.HasScope("vms:read").Should().BeFalse();
    }

    [Fact]
    public void ApiKey_HasScope_admin_wildcard_grants_everything()
    {
        var key = NewKey("*");

        key.HasScope("vms:write").Should().BeTrue();
        key.HasScope("anything-at-all").Should().BeTrue();
    }

    [Fact]
    public void ApiKey_HasScope_does_not_do_prefix_or_hierarchy_matching()
    {
        var key = NewKey("services:read");

        key.HasScope("services").Should().BeFalse();
        key.HasScope("services:write").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApiKey_HasScope_blank_is_false_even_for_admin(string scope)
        => NewKey("*").HasScope(scope).Should().BeFalse();

    // ---------- Role.HasScope ----------

    private static Role NewRole(params string[] scopes)
        => Role.CreateCustom("ops", "Ops", scopes, Now);

    [Fact]
    public void Role_HasScope_matches_exact_and_global_wildcard()
    {
        NewRole("vms:read").HasScope("vms:read").Should().BeTrue();
        NewRole("vms:read").HasScope("vms:write").Should().BeFalse();
        NewRole("*").HasScope("anything-at-all").Should().BeTrue();
    }

    [Fact]
    public void Role_HasScope_blank_is_false()
        => NewRole("*").HasScope("").Should().BeFalse();
}
