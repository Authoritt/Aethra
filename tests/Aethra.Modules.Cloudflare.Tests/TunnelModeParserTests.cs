using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.UseCases.Tunnels;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

/// <summary>OT-008 T15 #4: las tools MCP con <c>mode</c> reciben el literal "null" de los clientes; no es un modo invalido.</summary>
public sealed class TunnelModeParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("null")]
    [InlineData("NULL")]
    [InlineData(" None ")]
    [InlineData("undefined")]
    [InlineData("default")]
    public void Absent_mode_takes_the_default(string? raw)
    {
        var r = TunnelModeParser.Parse(raw);
        r.IsSuccess.Should().BeTrue();
        r.Value.Should().BeNull();
    }

    [Theory]
    [InlineData("direct", TunnelMode.Direct)]
    [InlineData("Direct", TunnelMode.Direct)]
    [InlineData("central_proxy", TunnelMode.CentralProxy)]
    [InlineData("central-proxy", TunnelMode.CentralProxy)]
    [InlineData("CentralProxy", TunnelMode.CentralProxy)]
    public void Known_modes_parse(string raw, TunnelMode expected)
        => TunnelModeParser.Parse(raw).Value.Should().Be(expected);

    [Theory]
    [InlineData("bogus")]
    [InlineData("directo")]
    public void Unknown_mode_is_still_rejected(string raw)
    {
        var r = TunnelModeParser.Parse(raw);
        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("cloudflare.tunnel_mode_invalid");
    }
}
