using Aethra.Modules.Services.UseCases.Commands;
using Aethra.Shared.Contracts.Services;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// Tests del validator <c>CreateBindingValidator</c>: required ServiceId/InstanceId y el
/// ResourceName OPCIONAL que, si viene, debe ser un identificador Postgres-safe
/// (<c>^[a-zA-Z_][a-zA-Z0-9_]{0,62}$</c>) — clave para evitar inyección en el nombre de DB/rol.
/// </summary>
public sealed class ServicesValidatorTests
{
    private static CreateBindingCommand New(
        string serviceId = "svc_1", string instanceId = "ins_1", string? resourceName = null)
        => new(serviceId, instanceId, resourceName, BindingPermissions.ReadWrite, null, null);

    [Fact]
    public void CreateBinding_accepts_valid_without_a_resource_name()
        => new CreateBindingValidator().Validate(New()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("my_db")]
    [InlineData("_private")]
    [InlineData("App2")]
    public void CreateBinding_accepts_postgres_safe_resource_names(string name)
        => new CreateBindingValidator().Validate(New(resourceName: name)).IsValid.Should().BeTrue();

    [Fact]
    public void CreateBinding_requires_service_id_and_instance_id()
    {
        new CreateBindingValidator().Validate(New(serviceId: "")).IsValid.Should().BeFalse();
        new CreateBindingValidator().Validate(New(instanceId: "")).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("1leading")]  // empieza con dígito
    [InlineData("with-dash")]
    [InlineData("with space")]
    [InlineData("with.dot")]
    public void CreateBinding_rejects_non_postgres_safe_resource_names(string name)
        => new CreateBindingValidator().Validate(New(resourceName: name)).IsValid.Should().BeFalse();

    [Fact]
    public void CreateBinding_rejects_resource_name_over_63_chars()
        => new CreateBindingValidator().Validate(New(resourceName: "a" + new string('b', 63))).IsValid.Should().BeFalse();
}
