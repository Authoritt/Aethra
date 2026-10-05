using System.Text;
using Aethra.Modules.Deployments.Webhooks;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>OT-012 (H3) — registro anti-replay del webhook de git.</summary>
public sealed class WebhookReplayGuardTests
{
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"after\":\"abc\"}");

    [Fact]
    public void Same_delivery_is_rejected_within_the_ttl()
    {
        var guard = new WebhookReplayGuard(new ManualClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(1));

        guard.TryRegister("tpl_a", "d1", Body).Should().BeTrue();
        guard.TryRegister("tpl_a", "d1", Body).Should().BeFalse();
    }

    [Fact]
    public void Same_body_under_a_forged_delivery_id_is_rejected()
    {
        var guard = new WebhookReplayGuard(new ManualClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(1));

        guard.TryRegister("tpl_a", "d1", Body).Should().BeTrue();
        guard.TryRegister("tpl_a", "forged", Body).Should().BeFalse();
        guard.TryRegister("tpl_a", null, Body).Should().BeFalse();
    }

    [Fact]
    public void Same_delivery_id_with_another_body_is_rejected()
    {
        var guard = new WebhookReplayGuard(new ManualClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(1));

        guard.TryRegister("tpl_a", "d1", Body).Should().BeTrue();
        guard.TryRegister("tpl_a", "d1", Encoding.UTF8.GetBytes("{\"after\":\"def\"}")).Should().BeFalse();
    }

    [Fact]
    public void Entries_expire_after_the_ttl()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var guard = new WebhookReplayGuard(clock, TimeSpan.FromHours(1));

        guard.TryRegister("tpl_a", "d1", Body).Should().BeTrue();
        clock.Now += TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1);

        guard.TryRegister("tpl_a", "d1", Body).Should().BeTrue();
    }

    [Fact]
    public void Forget_lets_a_failed_delivery_be_retried()
    {
        var guard = new WebhookReplayGuard(new ManualClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(1));

        guard.TryRegister("tpl_a", "d1", Body).Should().BeTrue();
        guard.Forget("tpl_a", "d1", Body);

        guard.TryRegister("tpl_a", "d1", Body).Should().BeTrue();
    }

    [Fact]
    public void Same_body_for_another_template_is_not_a_replay()
    {
        // G2 de OT-012: dos Templates del mismo repo+rama = dos webhooks con el MISMO cuerpo.
        var guard = new WebhookReplayGuard(new ManualClock(DateTimeOffset.UnixEpoch), TimeSpan.FromHours(1));

        guard.TryRegister("tpl_a", "d1", Body).Should().BeTrue();
        guard.TryRegister("tpl_b", "d2", Body).Should().BeTrue();
        guard.TryRegister("tpl_b", "d2", Body).Should().BeFalse();
    }
}
