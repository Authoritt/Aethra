using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Domain.Events;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Notifications.Tests;

/// <summary>
/// Invariantes del agregado <see cref="NotificationChannel"/>: validación de nombre/config,
/// normalización de filtros (dedup, blanks, validación contra el catálogo) y — lo central — la
/// decisión de routing <see cref="NotificationChannel.MatchesEvent"/> (inactivo nunca matchea,
/// filtros vacíos = todos, filtros declarados = match exacto).
/// </summary>
public sealed class NotificationChannelTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Cipher = [1, 2, 3];

    [Fact]
    public void Create_starts_active_with_no_filters_and_raises_event()
    {
        var channel = NotificationChannel.Create("Ops", NotificationChannelType.Slack, Cipher, null, Now);

        channel.IsActive.Should().BeTrue();
        channel.Name.Should().Be("Ops");
        channel.EventFilters.Should().BeEmpty();
        channel.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<NotificationChannelCreatedEvent>();
    }

    [Fact]
    public void Create_throws_on_empty_cipher()
    {
        var act = () => NotificationChannel.Create("Ops", NotificationChannelType.Slack, [], null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_on_blank_name(string name)
    {
        var act = () => NotificationChannel.Create(name, NotificationChannelType.Slack, Cipher, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_on_name_over_100_chars()
    {
        var act = () => NotificationChannel.Create(new string('n', 101), NotificationChannelType.Slack, Cipher, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_normalizes_filters_dedup_skip_blank_preserve_order()
    {
        var channel = NotificationChannel.Create("c", NotificationChannelType.Slack, Cipher,
            [NotificationEventTypes.MonitorDown, NotificationEventTypes.MonitorDown, "  ", NotificationEventTypes.BuildFailed],
            Now);

        channel.EventFilters.Should().Equal(NotificationEventTypes.MonitorDown, NotificationEventTypes.BuildFailed);
    }

    [Fact]
    public void Create_throws_on_unknown_event_filter()
    {
        var act = () => NotificationChannel.Create("c", NotificationChannelType.Slack, Cipher, ["bogus.event"], Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MatchesEvent_with_empty_filters_matches_all_when_active()
    {
        var channel = NotificationChannel.Create("c", NotificationChannelType.Slack, Cipher, null, Now);

        channel.MatchesEvent(NotificationEventTypes.MonitorDown).Should().BeTrue();
        channel.MatchesEvent("any.event").Should().BeTrue();
    }

    [Fact]
    public void MatchesEvent_with_filters_matches_only_declared_types()
    {
        var channel = NotificationChannel.Create("c", NotificationChannelType.Slack, Cipher,
            [NotificationEventTypes.MonitorDown], Now);

        channel.MatchesEvent(NotificationEventTypes.MonitorDown).Should().BeTrue();
        channel.MatchesEvent(NotificationEventTypes.BuildFailed).Should().BeFalse();
    }

    [Fact]
    public void MatchesEvent_is_false_when_inactive_even_with_empty_filters()
    {
        var channel = NotificationChannel.Create("c", NotificationChannelType.Slack, Cipher, null, Now);
        channel.SetActive(false, Now);

        channel.MatchesEvent(NotificationEventTypes.MonitorDown).Should().BeFalse();
    }

    [Fact]
    public void SetActive_is_idempotent_and_does_not_bump_updated_at()
    {
        var channel = NotificationChannel.Create("c", NotificationChannelType.Slack, Cipher, null, Now);
        var before = channel.UpdatedAt;

        channel.SetActive(true, Now.AddHours(1)); // ya está activo

        channel.UpdatedAt.Should().Be(before);
    }

    [Fact]
    public void UpdateEventFilters_replaces_the_filter_set()
    {
        var channel = NotificationChannel.Create("c", NotificationChannelType.Slack, Cipher,
            [NotificationEventTypes.MonitorDown], Now);

        channel.UpdateEventFilters([NotificationEventTypes.CertificateExpired], Now.AddMinutes(1));

        channel.EventFilters.Should().Equal(NotificationEventTypes.CertificateExpired);
    }

    [Fact]
    public void UpdateConfig_throws_on_empty_cipher()
    {
        var channel = NotificationChannel.Create("c", NotificationChannelType.Slack, Cipher, null, Now);

        var act = () => channel.UpdateConfig([], Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkDeleted_raises_deleted_event()
    {
        var channel = NotificationChannel.Create("c", NotificationChannelType.Slack, Cipher, null, Now);
        channel.ClearDomainEvents();

        channel.MarkDeleted();

        channel.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<NotificationChannelDeletedEvent>();
    }
}
