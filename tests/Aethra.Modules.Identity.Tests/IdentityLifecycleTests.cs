using Aethra.Modules.Identity.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// Ciclo de vida de <see cref="ApiKey"/> (IsActive/IsExpired/Revoke) y del agregado
/// <see cref="User"/> (normalización de email, validación, asignación de roles idempotente).
/// </summary>
public sealed class IdentityLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Scopes = ["services:read"];
    private static readonly byte[] Cipher = [1, 2, 3];

    // ---------- ApiKey lifecycle ----------

    private static ApiKey NewKey(DateTimeOffset? expiresAt = null)
        => ApiKey.Create("ci", "secret-plaintext", Scopes, Now, new ApiKeyHasher(), expiresAt);

    [Fact]
    public void ApiKey_is_active_when_neither_expired_nor_revoked()
    {
        var key = NewKey();

        key.IsRevoked.Should().BeFalse();
        key.IsExpired(Now).Should().BeFalse();
        key.IsActive(Now).Should().BeTrue();
    }

    [Fact]
    public void ApiKey_expires_once_past_the_expiry_instant()
    {
        var key = NewKey(expiresAt: Now.AddMinutes(30));

        key.IsExpired(Now).Should().BeFalse();
        key.IsActive(Now).Should().BeTrue();

        var later = Now.AddHours(1);
        key.IsExpired(later).Should().BeTrue(); // ExpiresAt (now+30m) <= now+1h
        key.IsActive(later).Should().BeFalse();
    }

    [Fact]
    public void ApiKey_create_rejects_an_expiry_in_the_past()
    {
        var act = () => NewKey(expiresAt: Now.AddMinutes(-1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ApiKey_revoke_deactivates_and_is_idempotent()
    {
        var key = NewKey();

        key.Revoke(Now);
        key.IsRevoked.Should().BeTrue();
        key.IsActive(Now).Should().BeFalse();

        key.ClearDomainEvents();
        key.Revoke(Now); // segunda vez: no-op
        key.DomainEvents.Should().BeEmpty();
    }

    // ---------- User aggregate ----------

    private static User NewUser(string email = "admin@example.com", string? displayName = null)
        => User.Create(email, Cipher, displayName, Now);

    [Fact]
    public void User_create_normalizes_email_trims_display_name_and_raises_event()
    {
        var user = User.Create("  Admin@Example.COM ", Cipher, "  Boss  ", Now);

        user.Email.Should().Be("admin@example.com");
        user.DisplayName.Should().Be("Boss");
        user.DomainEvents.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("noatsign")]
    [InlineData("@nolocal")]
    [InlineData("nodomain@")]
    public void User_create_rejects_invalid_email(string email)
    {
        var act = () => User.Create(email, Cipher, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void User_create_rejects_empty_password_cipher()
    {
        var act = () => User.Create("a@b.com", Array.Empty<byte>(), null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NormalizeEmail_trims_and_lowercases()
        => User.NormalizeEmail("  X@Y.COM ").Should().Be("x@y.com");

    [Fact]
    public void AssignRole_adds_the_role_and_is_idempotent()
    {
        var user = NewUser();
        var roleId = RoleId.New();

        user.AssignRole(roleId, Now);
        user.Roles.Should().ContainSingle(r => r.RoleId == roleId);

        user.AssignRole(roleId, Now); // idempotente
        user.Roles.Should().HaveCount(1);
    }

    [Fact]
    public void UnassignRole_removes_the_role()
    {
        var user = NewUser();
        var roleId = RoleId.New();
        user.AssignRole(roleId, Now);

        user.UnassignRole(roleId, Now);

        user.Roles.Should().BeEmpty();
    }
}
