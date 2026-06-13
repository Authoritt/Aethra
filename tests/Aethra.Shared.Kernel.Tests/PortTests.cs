using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Shared.Kernel.Tests;

/// <summary>
/// <see cref="Port"/> es el puerto TCP/UDP válido (1..65535) usado por PortMapping y los specs de
/// contenedor. Cubrimos el rango, la conversión implícita a int y el ToString invariante.
/// </summary>
public sealed class PortTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(8080)]
    [InlineData(65535)]
    public void Create_accepts_valid_ports(int value)
    {
        var result = Port.Create(value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(100000)]
    public void Create_rejects_out_of_range_ports(int value)
    {
        var result = Port.Create(value);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("port.range");
    }

    [Fact]
    public void Implicit_conversion_to_int_yields_the_value()
    {
        var port = Port.Create(8080).Value;

        int asInt = port;

        asInt.Should().Be(8080);
    }

    [Fact]
    public void ToString_renders_the_invariant_number()
    {
        Port.Create(5432).Value.ToString().Should().Be("5432");
    }
}
