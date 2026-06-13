using Aethra.Modules.Projects.UseCases.Clients.Commands;
using Aethra.Modules.Projects.UseCases.Projects.Commands;
using Aethra.Modules.Projects.UseCases.Templates.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

/// <summary>
/// Tests de los validators de actualización de Projects (UpdateProject/UpdateTemplate/UpdateClient):
/// required ids + nombres, BuildType enum (UpdateTemplate) y email condicional (UpdateClient).
/// </summary>
public sealed class ProjectsUpdateValidatorTests
{
    // ---------- UpdateProject ----------

    [Fact]
    public void UpdateProject_accepts_a_valid_command()
        => new UpdateProjectValidator().Validate(new UpdateProjectCommand("prj_1", "Name", null, null, null))
            .IsValid.Should().BeTrue();

    [Fact]
    public void UpdateProject_requires_id_and_name()
    {
        new UpdateProjectValidator().Validate(new UpdateProjectCommand("", "Name", null, null, null)).IsValid.Should().BeFalse();
        new UpdateProjectValidator().Validate(new UpdateProjectCommand("prj_1", "", null, null, null)).IsValid.Should().BeFalse();
    }

    // ---------- UpdateTemplate ----------

    private static UpdateTemplateCommand UT(
        string id = "tpl_1", string name = "Web", string url = "https://github.com/o/r",
        string branch = "main", string buildType = "Dockerfile")
        => new(id, name, null, url, branch, null, null, null, buildType, null, null, null);

    [Fact]
    public void UpdateTemplate_accepts_a_valid_command()
        => new UpdateTemplateValidator().Validate(UT()).IsValid.Should().BeTrue();

    [Fact]
    public void UpdateTemplate_requires_id_name_url_and_branch()
    {
        new UpdateTemplateValidator().Validate(UT(id: "")).IsValid.Should().BeFalse();
        new UpdateTemplateValidator().Validate(UT(name: "")).IsValid.Should().BeFalse();
        new UpdateTemplateValidator().Validate(UT(url: "")).IsValid.Should().BeFalse();
        new UpdateTemplateValidator().Validate(UT(branch: "")).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Nixpacks", true)]
    [InlineData("dockercompose", true)] // case-insensitive
    [InlineData("bogus", false)]
    [InlineData("", false)]
    public void UpdateTemplate_validates_build_type(string buildType, bool expectedValid)
        => new UpdateTemplateValidator().Validate(UT(buildType: buildType)).IsValid.Should().Be(expectedValid);

    // ---------- UpdateClient ----------

    private static UpdateClientCommand UC(string id = "cli_1", string name = "Acme", string? email = null)
        => new(id, name, null, email, null);

    [Fact]
    public void UpdateClient_accepts_valid_without_email()
        => new UpdateClientValidator().Validate(UC()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("ops@acme.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("   ", true)] // email en blanco se omite (opcional)
    public void UpdateClient_validates_optional_email(string email, bool expectedValid)
        => new UpdateClientValidator().Validate(UC(email: email)).IsValid.Should().Be(expectedValid);

    [Fact]
    public void UpdateClient_requires_id_and_display_name()
    {
        new UpdateClientValidator().Validate(UC(id: "")).IsValid.Should().BeFalse();
        new UpdateClientValidator().Validate(UC(name: "")).IsValid.Should().BeFalse();
    }
}
