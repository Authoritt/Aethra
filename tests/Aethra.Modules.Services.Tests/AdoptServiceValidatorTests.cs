using Aethra.Modules.Services.UseCases.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// Tests del <see cref="AdoptServiceValidator"/>: requeridos (slug/name/type/vm/container), formato de
/// slug y rango de puerto. El validator corre en el ValidationBehavior antes del handler de adopt.
/// </summary>
public sealed class AdoptServiceValidatorTests
{
    private static AdoptServiceCommand New(
        string slug = "aethra-postgres",
        string name = "Aethra Postgres",
        string type = "Postgres",
        string targetVmId = "vm_ABC",
        string containerName = "aethra-postgres",
        int internalPort = 5432)
        => new(
            Slug: slug,
            Name: name,
            Type: type,
            Version: "16",
            TargetVmId: targetVmId,
            ContainerName: containerName,
            Image: "postgres:16-alpine",
            InternalPort: internalPort,
            NetworkName: "aethra-net",
            AdminUser: "aethra",
            AdminPassword: "secret",
            ExposedExternally: false);

    private static readonly AdoptServiceValidator Validator = new();

    [Fact]
    public void Accepts_a_well_formed_command()
        => Validator.Validate(New()).IsValid.Should().BeTrue();

    [Fact]
    public void Accepts_internal_port_zero_meaning_type_default()
        => Validator.Validate(New(internalPort: 0)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("Aethra-Postgres")]   // mayúsculas
    [InlineData("1postgres")]          // empieza con dígito
    [InlineData("aethra_postgres")]    // guión bajo no permitido
    public void Rejects_invalid_slug(string slug)
        => Validator.Validate(New(slug: slug)).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_empty_name()
        => Validator.Validate(New(name: "")).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_empty_type()
        => Validator.Validate(New(type: "")).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_empty_target_vm()
        => Validator.Validate(New(targetVmId: "")).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_empty_container_name()
        => Validator.Validate(New(containerName: "")).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Rejects_port_out_of_range(int port)
        => Validator.Validate(New(internalPort: port)).IsValid.Should().BeFalse();
}
