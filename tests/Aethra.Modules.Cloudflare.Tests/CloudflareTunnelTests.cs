using Aethra.Modules.Cloudflare.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

/// <summary>
/// Invariantes del agregado <see cref="CloudflareTunnel"/>: validación de campos + token,
/// defaults de servicios, y la asimetría de <see cref="CloudflareTunnel.UpdateServices"/>
/// (aethraService en blanco se preserva; fallbackService vacío SÍ se aplica = catch-all 404).
/// </summary>
public sealed class CloudflareTunnelTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Token = [1, 2, 3];

    [Fact]
    public void Create_trims_and_applies_service_defaults()
    {
        var tunnel = CloudflareTunnel.Create("  tun-1 ", " authorit-apps ", " acct-9 ", Token, null, null, false, Now);

        tunnel.TunnelId.Should().Be("tun-1");
        tunnel.Name.Should().Be("authorit-apps");
        tunnel.AccountId.Should().Be("acct-9");
        tunnel.AethraService.Should().Be("http://localhost:5080");
        tunnel.FallbackService.Should().Be("https://localhost:443");
    }

    [Fact]
    public void Create_honors_custom_services()
    {
        var tunnel = CloudflareTunnel.Create("tun", "n", "acct", Token, "http://localhost:9000", "http://fallback:8443", true, Now);

        tunnel.AethraService.Should().Be("http://localhost:9000");
        tunnel.FallbackService.Should().Be("http://fallback:8443");
        tunnel.FallbackNoTlsVerify.Should().BeTrue();
    }

    [Fact]
    public void Create_blank_fallback_falls_back_to_the_default_unlike_UpdateServices()
    {
        // En Create, fallback en blanco usa el default (IsNullOrWhiteSpace); en UpdateServices,
        // "" sí se aplica (is not null). Documentamos ambas semánticas.
        var tunnel = CloudflareTunnel.Create("tun", "n", "acct", Token, null, "", false, Now);

        tunnel.FallbackService.Should().Be("https://localhost:443");
    }

    [Theory]
    [InlineData("", "n", "acct")]
    [InlineData("tun", "", "acct")]
    [InlineData("tun", "n", "")]
    public void Create_throws_on_blank_required_fields(string tunnelId, string name, string accountId)
    {
        var act = () => CloudflareTunnel.Create(tunnelId, name, accountId, Token, null, null, false, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_on_empty_token()
    {
        var act = () => CloudflareTunnel.Create("tun", "n", "acct", [], null, null, false, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateServices_preserves_aethra_on_blank_but_applies_empty_fallback()
    {
        var tunnel = CloudflareTunnel.Create("tun", "n", "acct", Token, "http://a:1", "http://f:2", false, Now);

        tunnel.UpdateServices("   ", "", true, Now);

        tunnel.AethraService.Should().Be("http://a:1");
        tunnel.FallbackService.Should().BeEmpty();
        tunnel.FallbackNoTlsVerify.Should().BeTrue();
    }

    [Fact]
    public void SetTargetVm_trims_and_treats_blank_as_null()
    {
        var tunnel = CloudflareTunnel.Create("tun", "n", "acct", Token, null, null, false, Now);

        tunnel.SetTargetVm("  vm_1 ", Now);
        tunnel.TargetVmId.Should().Be("vm_1");

        tunnel.SetTargetVm("   ", Now);
        tunnel.TargetVmId.Should().BeNull();
    }

    [Fact]
    public void UpdateToken_throws_on_empty()
    {
        var tunnel = CloudflareTunnel.Create("tun", "n", "acct", Token, null, null, false, Now);

        var act = () => tunnel.UpdateToken([], Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkSynced_sets_last_synced_at()
    {
        var tunnel = CloudflareTunnel.Create("tun", "n", "acct", Token, null, null, false, Now);

        tunnel.MarkSynced(Now.AddMinutes(1));

        tunnel.LastSyncedAt.Should().Be(Now.AddMinutes(1));
    }
}
