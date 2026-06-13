using Aethra.Modules.Settings.Domain;
using Aethra.Modules.Settings.Domain.Events;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Settings.Tests;

/// <summary>
/// Invariantes del agregado <see cref="BaseDomain"/>: validación/normalización del FQDN base,
/// la invariante "solo uno activo" a nivel de aggregate (Activate/Deactivate idempotentes con
/// su evento), y los flags wildcard + link de zona Cloudflare.
/// </summary>
public sealed class BaseDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_hostname_and_starts_inactive()
    {
        var domain = BaseDomain.Create("  Aethra.Example.COM  ", "  zone-1 ", Now);

        domain.Hostname.Should().Be("aethra.example.com");
        domain.CloudflareZoneId.Should().Be("zone-1");
        domain.IsActive.Should().BeFalse();
        domain.WildcardConfigured.Should().BeFalse();
        domain.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<BaseDomainCreatedEvent>();
    }

    [Fact]
    public void Create_treats_blank_or_null_zone_id_as_null()
    {
        BaseDomain.Create("a.example.com", "   ", Now).CloudflareZoneId.Should().BeNull();
        BaseDomain.Create("a.example.com", null, Now).CloudflareZoneId.Should().BeNull();
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("a.b.example.com")]
    [InlineData("sub.dom-ain.co")]
    public void Create_accepts_valid_fqdns(string host)
    {
        BaseDomain.Create(host, null, Now).Hostname.Should().Be(host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("single")]        // un solo label, requiere >= 2
    [InlineData("-leading.com")]
    [InlineData("trailing-.com")]
    [InlineData("under_score.com")]
    [InlineData("spa ce.com")]
    public void Create_rejects_invalid_hostnames(string host)
    {
        var act = () => BaseDomain.Create(host, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_hostname_over_253_chars()
    {
        var act = () => BaseDomain.Create(new string('a', 250) + ".example.com", null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Activate_sets_active_and_raises_event_once()
    {
        var domain = BaseDomain.Create("a.example.com", null, Now);
        domain.ClearDomainEvents();

        domain.Activate(Now);
        domain.IsActive.Should().BeTrue();
        domain.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<BaseDomainActivatedEvent>();

        domain.ClearDomainEvents();
        domain.Activate(Now); // idempotente
        domain.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Deactivate_clears_active_and_is_idempotent()
    {
        var domain = BaseDomain.Create("a.example.com", null, Now);
        domain.Activate(Now);

        domain.Deactivate(Now);
        domain.IsActive.Should().BeFalse();

        domain.Deactivate(Now); // idempotente, sin error
        domain.IsActive.Should().BeFalse();
    }

    [Fact]
    public void MarkWildcardConfigured_sets_the_flag_idempotently()
    {
        var domain = BaseDomain.Create("a.example.com", null, Now);

        domain.MarkWildcardConfigured(Now);
        domain.WildcardConfigured.Should().BeTrue();

        domain.MarkWildcardConfigured(Now); // no-op
        domain.WildcardConfigured.Should().BeTrue();
    }

    [Fact]
    public void LinkCloudflareZone_sets_then_clears_the_zone()
    {
        var domain = BaseDomain.Create("a.example.com", null, Now);

        domain.LinkCloudflareZone("  zone-9 ", Now);
        domain.CloudflareZoneId.Should().Be("zone-9");

        domain.LinkCloudflareZone("   ", Now);
        domain.CloudflareZoneId.Should().BeNull();
    }
}
