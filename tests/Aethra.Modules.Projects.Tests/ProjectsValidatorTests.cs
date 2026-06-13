using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Modules.Projects.UseCases.Projects.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

/// <summary>
/// Tests de los validators FluentValidation de Projects — el slug regex de CreateProject
/// (<c>^[a-z][a-z0-9-]{0,30}$</c>) y los required/length de CreateInstance. Puros, sin BD.
/// </summary>
public sealed class ProjectsValidatorTests
{
    // ---------- CreateProject ----------

    [Theory]
    [InlineData("my-app")]
    [InlineData("backend")]
    [InlineData("a")]
    [InlineData("app2")]
    public void CreateProject_accepts_valid_slugs(string slug)
    {
        new CreateProjectValidator().Validate(new CreateProjectCommand(slug, "Name", null, null, null))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]        // vacío
    [InlineData("MyApp")]   // mayúsculas
    [InlineData("1app")]    // empieza con dígito
    [InlineData("-app")]    // empieza con guion
    [InlineData("my_app")]  // underscore
    [InlineData("my app")]  // espacio
    public void CreateProject_rejects_invalid_slugs(string slug)
    {
        new CreateProjectValidator().Validate(new CreateProjectCommand(slug, "Name", null, null, null))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateProject_rejects_slug_over_31_chars()
    {
        new CreateProjectValidator().Validate(new CreateProjectCommand("a" + new string('b', 31), "Name", null, null, null))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateProject_requires_name()
    {
        var result = new CreateProjectValidator().Validate(new CreateProjectCommand("app", "", null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void CreateProject_rejects_description_over_2000_chars()
    {
        new CreateProjectValidator().Validate(new CreateProjectCommand("app", "Name", new string('d', 2001), null, null))
            .IsValid.Should().BeFalse();
    }

    // ---------- CreateInstance ----------

    private static CreateInstanceCommand NewInstance(
        string templateId = "tpl_1", string clientId = "cli_1", string env = "production", string vm = "vm_1")
        => new(templateId, clientId, env, vm, null, null, null, null, false);

    [Fact]
    public void CreateInstance_accepts_a_valid_command()
    {
        new CreateInstanceValidator().Validate(NewInstance()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateInstance_requires_template_client_environment_and_vm()
    {
        new CreateInstanceValidator().Validate(NewInstance(templateId: "")).IsValid.Should().BeFalse();
        new CreateInstanceValidator().Validate(NewInstance(clientId: "")).IsValid.Should().BeFalse();
        new CreateInstanceValidator().Validate(NewInstance(env: "")).IsValid.Should().BeFalse();
        new CreateInstanceValidator().Validate(NewInstance(vm: "")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateInstance_rejects_environment_over_32_chars()
    {
        new CreateInstanceValidator().Validate(NewInstance(env: new string('e', 33))).IsValid.Should().BeFalse();
    }
}
