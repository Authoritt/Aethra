using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Domain.Events;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

/// <summary>
/// Invariantes del agregado <see cref="CloudflareZone"/>: Create (name lowercased, zoneId solo
/// trim, Status=Unknown, evento), UpdateToken (rota cipher + evento), UpdateFromSync (status/name/
/// account + LastSyncedAt) y MarkSynced (no cambia el status).
/// </summary>
public sealed class CloudflareZoneTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Token = [1, 2, 3];

    private static CloudflareZone NewZone()
        => CloudflareZone.Create("zone123", "example.com", "acct-9", Token, Now);

    [Fact]
    public void Create_lowercases_name_trims_zone_id_and_starts_unknown_with_event()
    {
        var zone = CloudflareZone.Create("  ZONE123 ", "  Example.COM ", " acct-9 ", Token, Now);

        zone.ZoneId.Should().Be("ZONE123"); // solo trim, no lowercase
        zone.Name.Should().Be("example.com"); // lowercased
        zone.AccountId.Should().Be("acct-9");
        zone.Status.Should().Be(CloudflareZoneStatus.Unknown);
        zone.LastSyncedAt.Should().BeNull();
        zone.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CloudflareZoneRegisteredEvent>();
    }

    [Theory]
    [InlineData("", "name", "acct")]
    [InlineData("zone", "", "acct")]
    [InlineData("zone", "name", "")]
    public void Create_throws_on_blank_required_fields(string zoneId, string name, string accountId)
    {
        var act = () => CloudflareZone.Create(zoneId, name, accountId, Token, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_on_empty_token()
    {
        var act = () => CloudflareZone.Create("zone", "name", "acct", [], Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateToken_replaces_cipher_and_raises_rotated_event()
    {
        var zone = NewZone();
        zone.ClearDomainEvents();
        var newToken = new byte[] { 9, 9, 9 };

        zone.UpdateToken(newToken, Now.AddMinutes(1));

        zone.ApiTokenCipher.Should().Equal(newToken);
        zone.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CloudflareZoneTokenRotatedEvent>();
    }

    [Fact]
    public void UpdateToken_throws_on_empty()
    {
        var zone = NewZone();

        var act = () => zone.UpdateToken([], Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateFromSync_updates_status_name_account_and_marks_synced()
    {
        var zone = NewZone();

        zone.UpdateFromSync(CloudflareZoneStatus.Active, "  New.Example.COM ", " acct-2 ", Now.AddMinutes(1));

        zone.Status.Should().Be(CloudflareZoneStatus.Active);
        zone.Name.Should().Be("new.example.com");
        zone.AccountId.Should().Be("acct-2");
        zone.LastSyncedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void UpdateFromSync_throws_on_blank_name()
    {
        var zone = NewZone();

        var act = () => zone.UpdateFromSync(CloudflareZoneStatus.Active, "   ", "acct", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkSynced_sets_last_synced_without_changing_status()
    {
        var zone = NewZone(); // Unknown

        zone.MarkSynced(Now.AddMinutes(1));

        zone.LastSyncedAt.Should().Be(Now.AddMinutes(1));
        zone.Status.Should().Be(CloudflareZoneStatus.Unknown);
    }
}
