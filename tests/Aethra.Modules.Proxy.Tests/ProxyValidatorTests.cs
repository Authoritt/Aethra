using Aethra.Modules.Proxy.UseCases.Routes.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Proxy.Tests;

/// <summary>
/// Tests de los validators FluentValidation de las rutas del proxy — guardas de longitud/no-vacío
/// en la capa de comando (la validación de formato de URL absoluta vive en el dominio Route).
/// </summary>
public sealed class ProxyValidatorTests
{
    [Fact]
    public void CreateRoute_accepts_a_valid_command()
    {
        new CreateRouteValidator()
            .Validate(new CreateRouteCommand("app.example.com", "https://backend:8080", true))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "https://b:1")]      // hostname vacío
    [InlineData("app.example.com", "")]  // backend vacío
    public void CreateRoute_rejects_empty_hostname_or_backend(string hostname, string backend)
    {
        new CreateRouteValidator()
            .Validate(new CreateRouteCommand(hostname, backend, false))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateRoute_rejects_hostname_over_253_chars()
    {
        new CreateRouteValidator()
            .Validate(new CreateRouteCommand(new string('a', 254), "https://b:1", false))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateRoute_rejects_backend_url_over_512_chars()
    {
        var longUrl = "https://" + new string('a', 510); // 518 chars

        new CreateRouteValidator()
            .Validate(new CreateRouteCommand("app.example.com", longUrl, false))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateRoute_accepts_a_valid_command()
    {
        new UpdateRouteValidator()
            .Validate(new UpdateRouteCommand("rt_1", "https://backend:8080", false))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateRoute_rejects_empty_backend()
    {
        new UpdateRouteValidator()
            .Validate(new UpdateRouteCommand("rt_1", "", false))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateRoute_rejects_backend_url_over_512_chars()
    {
        new UpdateRouteValidator()
            .Validate(new UpdateRouteCommand("rt_1", "https://" + new string('a', 510), false))
            .IsValid.Should().BeFalse();
    }
}
