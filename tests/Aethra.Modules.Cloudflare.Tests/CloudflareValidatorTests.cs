using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Modules.Cloudflare.UseCases.Zones.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

/// <summary>
/// Tests de los validators FluentValidation de Cloudflare: tipo de DNS record (A|AAAA|CNAME|TXT|MX),
/// FQDN del name, rango de TTL, y los formatos hex-32 (zone/account id) + UUID-36 (tunnel id) +
/// longitud mínima de token.
/// </summary>
public sealed class CloudflareValidatorTests
{
    private const string Hex32 = "0123456789abcdef0123456789abcdef";
    private const string Uuid36 = "12345678-1234-1234-1234-123456789012";

    // ---------- CreateDnsRecord ----------

    private static CreateDnsRecordCommand NewDns(
        string type = "A", string name = "app.example.com", string content = "1.2.3.4", int ttl = 300)
        => new("zone-1", type, name, content, ttl, false, null);

    [Fact]
    public void CreateDnsRecord_accepts_a_valid_command()
        => new CreateDnsRecordValidator().Validate(NewDns()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("A")]
    [InlineData("aaaa")] // case-insensitive
    [InlineData("CNAME")]
    [InlineData("TXT")]
    [InlineData("MX")]
    public void CreateDnsRecord_accepts_allowed_types(string type)
        => new CreateDnsRecordValidator().Validate(NewDns(type: type)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("SRV")]
    [InlineData("")]
    public void CreateDnsRecord_rejects_invalid_type(string type)
        => new CreateDnsRecordValidator().Validate(NewDns(type: type)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("not a fqdn")]
    [InlineData("nodot")]
    public void CreateDnsRecord_rejects_invalid_name(string name)
        => new CreateDnsRecordValidator().Validate(NewDns(name: name)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(86401)]
    public void CreateDnsRecord_rejects_ttl_out_of_range(int ttl)
        => new CreateDnsRecordValidator().Validate(NewDns(ttl: ttl)).IsValid.Should().BeFalse();

    // ---------- RegisterZone ----------

    [Fact]
    public void RegisterZone_accepts_a_valid_command()
        => new RegisterZoneValidator().Validate(new RegisterZoneCommand(Hex32, "tokentoken")).IsValid.Should().BeTrue();

    [Fact]
    public void RegisterZone_rejects_wrong_length_zone_id()
        => new RegisterZoneValidator().Validate(new RegisterZoneCommand("abc", "tokentoken")).IsValid.Should().BeFalse();

    [Fact]
    public void RegisterZone_rejects_non_hex_zone_id()
        => new RegisterZoneValidator().Validate(new RegisterZoneCommand(new string('z', 32), "tokentoken")).IsValid.Should().BeFalse();

    [Fact]
    public void RegisterZone_rejects_short_token()
        => new RegisterZoneValidator().Validate(new RegisterZoneCommand(Hex32, "short")).IsValid.Should().BeFalse();

    // ---------- RotateZoneToken ----------

    [Fact]
    public void RotateZoneToken_accepts_a_valid_command()
        => new RotateZoneTokenValidator().Validate(new RotateZoneTokenCommand("zone-1", "tokentoken")).IsValid.Should().BeTrue();

    [Fact]
    public void RotateZoneToken_rejects_short_token()
        => new RotateZoneTokenValidator().Validate(new RotateZoneTokenCommand("zone-1", "short")).IsValid.Should().BeFalse();

    // ---------- RegisterTunnel ----------

    private static RegisterTunnelCommand NewTunnel(
        string accountId = Hex32, string tunnelId = Uuid36, string name = "authorit-apps", string apiToken = "tokentoken")
        => new(accountId, tunnelId, name, apiToken, null, null, false);

    [Fact]
    public void RegisterTunnel_accepts_a_valid_command()
        => new RegisterTunnelValidator().Validate(NewTunnel()).IsValid.Should().BeTrue();

    [Fact]
    public void RegisterTunnel_rejects_bad_account_id()
        => new RegisterTunnelValidator().Validate(NewTunnel(accountId: "short")).IsValid.Should().BeFalse();

    [Fact]
    public void RegisterTunnel_rejects_bad_tunnel_id()
        => new RegisterTunnelValidator().Validate(NewTunnel(tunnelId: "not-a-uuid")).IsValid.Should().BeFalse();

    [Fact]
    public void RegisterTunnel_rejects_short_token()
        => new RegisterTunnelValidator().Validate(NewTunnel(apiToken: "short")).IsValid.Should().BeFalse();
}
