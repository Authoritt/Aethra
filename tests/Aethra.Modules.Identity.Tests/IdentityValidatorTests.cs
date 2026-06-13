using Aethra.Modules.Identity.UseCases.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

/// <summary>
/// Tests de los validators FluentValidation de Identity — la primera línea de defensa de los
/// comandos (email/password, longitudes, scopes y roles obligatorios). Son puros: no tocan BD.
/// </summary>
public sealed class IdentityValidatorTests
{
    // ---------- CreateUser ----------

    [Fact]
    public void CreateUser_accepts_a_valid_command()
    {
        var cmd = new CreateUserCommand("admin@example.com", "password1", "Admin", ["admin"]);

        new CreateUserValidator().Validate(cmd).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "password1")]    // email vacío
    [InlineData("a@b.com", "short")] // password < 8
    [InlineData("a@b.com", "")]      // password vacío
    public void CreateUser_rejects_bad_email_or_password(string email, string password)
    {
        var cmd = new CreateUserCommand(email, password, null, ["admin"]);

        new CreateUserValidator().Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateUser_requires_at_least_one_role()
    {
        var cmd = new CreateUserCommand("a@b.com", "password1", null, []);

        var result = new CreateUserValidator().Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RoleSlugs");
    }

    // ---------- CreateRole ----------

    [Fact]
    public void CreateRole_accepts_a_valid_command()
    {
        new CreateRoleValidator().Validate(new CreateRoleCommand("ops", "Ops", ["scope:x"]))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateRole_requires_at_least_one_scope()
    {
        var result = new CreateRoleValidator().Validate(new CreateRoleCommand("ops", "Ops", []));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Scopes");
    }

    [Fact]
    public void CreateRole_rejects_slug_over_64_chars()
    {
        new CreateRoleValidator().Validate(new CreateRoleCommand(new string('s', 65), "Ops", ["scope:x"]))
            .IsValid.Should().BeFalse();
    }

    // ---------- CreateApiKey ----------

    [Fact]
    public void CreateApiKey_accepts_a_valid_command()
    {
        new CreateApiKeyValidator().Validate(new CreateApiKeyCommand("ci-key", ["scope:x"], null))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateApiKey_requires_name_and_scopes()
    {
        new CreateApiKeyValidator().Validate(new CreateApiKeyCommand("", ["scope:x"], null)).IsValid.Should().BeFalse();
        new CreateApiKeyValidator().Validate(new CreateApiKeyCommand("ci", [], null)).IsValid.Should().BeFalse();
    }

    // ---------- ResetUserPassword ----------

    [Theory]
    [InlineData("", false)]
    [InlineData("short", false)]
    [InlineData("password1", true)]
    public void ResetUserPassword_enforces_min_password_length(string newPassword, bool expectedValid)
    {
        new ResetUserPasswordValidator().Validate(new ResetUserPasswordCommand("usr_1", newPassword))
            .IsValid.Should().Be(expectedValid);
    }

    // ---------- UpdateRole ----------

    [Fact]
    public void UpdateRole_accepts_a_valid_command()
    {
        new UpdateRoleValidator().Validate(new UpdateRoleCommand("rol_1", "Ops", ["scope:x"]))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateRole_requires_display_name_and_scopes()
    {
        new UpdateRoleValidator().Validate(new UpdateRoleCommand("rol_1", "", ["scope:x"])).IsValid.Should().BeFalse();
        new UpdateRoleValidator().Validate(new UpdateRoleCommand("rol_1", "Ops", [])).IsValid.Should().BeFalse();
    }
}
