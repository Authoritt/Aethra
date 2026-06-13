using Aethra.Modules.Notifications.Domain;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Notifications.Tests;

/// <summary>
/// Invariantes de <see cref="NotificationDelivery"/>: la cola arranca Pending con NextAttemptAt=now,
/// y la máquina de reintentos (Sent / AttemptFailed sigue Pending / PermanentlyFailed) cuenta
/// <c>Attempts</c>, gestiona <c>NextAttemptAt</c> y trunca el error.
/// </summary>
public sealed class NotificationDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static NotificationChannelId Channel => NotificationChannelId.New();

    [Fact]
    public void Queue_starts_pending_with_next_attempt_now_and_zero_attempts()
    {
        var delivery = NotificationDelivery.Queue(Channel, "monitor.down", "{}", Now);

        delivery.Status.Should().Be(NotificationDeliveryStatus.Pending);
        delivery.Attempts.Should().Be(0);
        delivery.NextAttemptAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Queue_throws_on_blank_event_type(string eventType)
    {
        var act = () => NotificationDelivery.Queue(Channel, eventType, "{}", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Queue_treats_null_payload_as_empty()
    {
        var delivery = NotificationDelivery.Queue(Channel, "monitor.down", null!, Now);

        delivery.Payload.Should().BeEmpty();
    }

    [Fact]
    public void MarkSent_sets_sent_increments_attempts_and_clears_next_attempt()
    {
        var delivery = NotificationDelivery.Queue(Channel, "e", "p", Now);

        delivery.MarkSent(Now.AddSeconds(1));

        delivery.Status.Should().Be(NotificationDeliveryStatus.Sent);
        delivery.Attempts.Should().Be(1);
        delivery.SentAt.Should().Be(Now.AddSeconds(1));
        delivery.Error.Should().BeNull();
        delivery.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void MarkAttemptFailed_increments_attempts_keeps_pending_and_schedules_next()
    {
        var delivery = NotificationDelivery.Queue(Channel, "e", "p", Now);
        var next = Now.AddMinutes(1);

        delivery.MarkAttemptFailed("boom", next, Now);

        delivery.Attempts.Should().Be(1);
        delivery.Status.Should().Be(NotificationDeliveryStatus.Pending);
        delivery.NextAttemptAt.Should().Be(next);
        delivery.Error.Should().Be("boom");
    }

    [Fact]
    public void MarkPermanentlyFailed_sets_failed_and_clears_next_attempt()
    {
        var delivery = NotificationDelivery.Queue(Channel, "e", "p", Now);
        delivery.MarkAttemptFailed("e1", Now.AddMinutes(1), Now);

        delivery.MarkPermanentlyFailed("final", Now.AddMinutes(2));

        delivery.Status.Should().Be(NotificationDeliveryStatus.Failed);
        delivery.Attempts.Should().Be(2);
        delivery.NextAttemptAt.Should().BeNull();
        delivery.Error.Should().Be("final");
    }

    [Fact]
    public void MarkAttemptFailed_truncates_a_long_error_to_2000_chars()
    {
        var delivery = NotificationDelivery.Queue(Channel, "e", "p", Now);

        delivery.MarkAttemptFailed(new string('x', 3000), null, Now);

        delivery.Error.Should().NotBeNull();
        delivery.Error!.Length.Should().Be(2000);
    }
}
