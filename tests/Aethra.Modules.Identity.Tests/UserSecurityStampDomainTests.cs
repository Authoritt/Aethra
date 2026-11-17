using Aethra.Modules.Identity.Domain;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class UserSecurityStampDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_user_generates_initial_non_empty_security_stamp()
    {
        var user = User.Create("alice@example.com", [1, 2, 3], "Alice", Now);

        user.SecurityStamp.Should().NotBeNullOrWhiteSpace();
        user.SecurityStamp.Length.Should().Be(32);
    }

    [Fact]
    public void ResetPassword_rotates_security_stamp()
    {
        var user = User.Create("alice@example.com", [1, 2, 3], "Alice", Now);
        var initialStamp = user.SecurityStamp;

        user.ResetPassword([4, 5, 6], Now.AddMinutes(1));

        user.SecurityStamp.Should().NotBe(initialStamp);
        user.SecurityStamp.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Deactivate_and_Reactivate_rotate_security_stamp()
    {
        var user = User.Create("alice@example.com", [1, 2, 3], "Alice", Now);
        var initialStamp = user.SecurityStamp;

        user.Deactivate(Now.AddMinutes(1));
        var deactivatedStamp = user.SecurityStamp;
        deactivatedStamp.Should().NotBe(initialStamp);

        // Si ya está inactivo, una segunda llamada no rota
        user.Deactivate(Now.AddMinutes(2));
        user.SecurityStamp.Should().Be(deactivatedStamp);

        user.Reactivate(Now.AddMinutes(3));
        user.SecurityStamp.Should().NotBe(deactivatedStamp);
    }

    [Fact]
    public void Role_changes_do_not_rotate_security_stamp()
    {
        // Los cambios de roles los aplica CookieSessionValidator reconstruyendo roles y scopes desde
        // la BD: no hace falta (ni conviene) cerrar la sesion entera.
        var user = User.Create("alice@example.com", [1, 2, 3], "Alice", Now);
        var initialStamp = user.SecurityStamp;
        var roleId = RoleId.New();

        user.AssignRole(roleId, Now.AddMinutes(1));
        user.UnassignRole(roleId, Now.AddMinutes(2));
        user.ReplaceRoles([RoleId.New()], Now.AddMinutes(3));
        user.ReplaceRoles(user.Roles.Select(r => r.RoleId).ToArray(), Now.AddMinutes(4));

        user.SecurityStamp.Should().Be(initialStamp);
    }

    [Fact]
    public void CompleteTotpEnrollment_and_DisableTotp_rotate_security_stamp()
    {
        var user = User.Create("alice@example.com", [1, 2, 3], "Alice", Now);
        var initialStamp = user.SecurityStamp;

        user.BeginTotpEnrollment([10, 20, 30], Now.AddMinutes(1));
        // Comenzar enrollment no confirma 2FA, stamp no rota
        user.SecurityStamp.Should().Be(initialStamp);

        user.CompleteTotpEnrollment([40, 50, 60], Now.AddMinutes(2));
        var totpEnabledStamp = user.SecurityStamp;
        totpEnabledStamp.Should().NotBe(initialStamp);

        user.DisableTotp(Now.AddMinutes(3));
        user.SecurityStamp.Should().NotBe(totpEnabledStamp);
    }

    [Fact]
    public void RotateSecurityStamp_explicit_method_rotates_security_stamp()
    {
        var user = User.Create("alice@example.com", [1, 2, 3], "Alice", Now);
        var initialStamp = user.SecurityStamp;

        user.RotateSecurityStamp(Now.AddMinutes(1));

        user.SecurityStamp.Should().NotBe(initialStamp);
    }

    [Fact]
    public void Non_security_methods_do_not_rotate_security_stamp()
    {
        var user = User.Create("alice@example.com", [1, 2, 3], "Alice", Now);
        var initialStamp = user.SecurityStamp;

        user.UpdateDisplayName("Alice Updated", Now.AddMinutes(1));
        user.SecurityStamp.Should().Be(initialStamp, "UpdateDisplayName no toca la postura de seguridad");

        user.SetGitHubUsername("alice-gh", Now.AddMinutes(2));
        user.SecurityStamp.Should().Be(initialStamp, "SetGitHubUsername no toca la postura de seguridad");

        user.MarkLogin(Now.AddMinutes(3));
        user.SecurityStamp.Should().Be(initialStamp, "MarkLogin no toca la postura de seguridad");
    }
}
