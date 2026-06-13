using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Shared.Kernel.Tests;

/// <summary>
/// <see cref="Hostname"/> es el FQDN que valida toda ruta del proxy y todo certificado TLS.
/// Cubrimos la aceptación/normalización (lowercase + trim), el rechazo de formatos inválidos
/// (sin TLD, label mal formado, TLD de 1 char, punto final, doble wildcard…), el límite de 253
/// caracteres y la detección de wildcard (<c>*.</c>).
/// </summary>
public sealed class HostnameTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("app.example.com", "app.example.com")]
    [InlineData("*.example.com", "*.example.com")]
    [InlineData("a.io", "a.io")]
    [InlineData("sub.dom-ain.co", "sub.dom-ain.co")]
    [InlineData("EXAMPLE.COM", "example.com")]               // lowercased
    [InlineData("  app.example.com  ", "app.example.com")]   // trimmed
    [InlineData("deep.sub.example.com", "deep.sub.example.com")]
    public void Create_accepts_and_normalizes_valid_hostnames(string input, string expected)
    {
        var result = Hostname.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("", "hostname.empty")]
    [InlineData("   ", "hostname.empty")]
    [InlineData("nodot", "hostname.format")]
    [InlineData("example", "hostname.format")]
    [InlineData("-leading.com", "hostname.format")]
    [InlineData("trailing-.com", "hostname.format")]
    [InlineData("example.com.", "hostname.format")]   // punto final
    [InlineData("a.b", "hostname.format")]            // TLD de 1 char
    [InlineData("under_score.com", "hostname.format")]
    [InlineData("spa ce.com", "hostname.format")]
    [InlineData("café.com", "hostname.format")]
    [InlineData("*.*.com", "hostname.format")]        // doble wildcard
    [InlineData("*example.com", "hostname.format")]   // wildcard sin punto
    public void Create_rejects_invalid_hostnames_with_the_right_error_code(string input, string expectedCode)
    {
        var result = Hostname.Create(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void Create_rejects_a_hostname_longer_than_253_chars()
    {
        var result = Hostname.Create(new string('a', 250) + ".example.com");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("hostname.length");
    }

    [Theory]
    [InlineData("*.example.com", true)]
    [InlineData("example.com", false)]
    [InlineData("app.example.com", false)]
    public void IsWildcard_detects_a_leading_wildcard(string input, bool expected)
    {
        Hostname.Create(input).Value.IsWildcard.Should().Be(expected);
    }
}
