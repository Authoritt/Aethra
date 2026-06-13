using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Modules.Deployments.Domain.Deployment.Events;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// Invariantes del agregado <see cref="Deployment"/>: la state machine del swap
/// (Pending→Pulling→Starting→Healthcheck→Swapping→Completed), Complete (requiere NewContainerId),
/// Fail idempotente, Cancel solo en estados tempranos, y — lo crítico — Rollback solo desde Failed
/// y solo si se capturó el contenedor previo (OldContainerId).
/// </summary>
public sealed class DeploymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Deployment NewDeployment()
        => Deployment.Queue("bld_1", "ins_1", "registry/app:abc", DeploymentTrigger.BuildAutomatic, null, Now);

    private static Deployment AtSwapping(bool captureOld)
    {
        var d = NewDeployment();
        d.Transition(DeploymentStatus.Pulling, Now);
        if (captureOld)
        {
            d.RecordOldContainer("cont_old", "registry/app:prev", Now);
        }
        d.Transition(DeploymentStatus.Starting, Now);
        d.Transition(DeploymentStatus.Healthcheck, Now);
        d.Transition(DeploymentStatus.Swapping, Now);
        return d;
    }

    [Fact]
    public void Queue_starts_pending_logs_and_raises_event()
    {
        var d = NewDeployment();

        d.Status.Should().Be(DeploymentStatus.Pending);
        d.NewImageRef.Should().Be("registry/app:abc");
        d.Logs.Should().ContainSingle();
        d.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<DeploymentQueuedEvent>();
    }

    [Theory]
    [InlineData("", "ins", "img")]
    [InlineData("bld", "", "img")]
    [InlineData("bld", "ins", "")]
    public void Queue_throws_on_blank_required_fields(string buildId, string instanceId, string imageRef)
    {
        var act = () => Deployment.Queue(buildId, instanceId, imageRef, DeploymentTrigger.Manual, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Transition_happy_path_sets_started_at_on_pulling()
    {
        var d = NewDeployment();

        d.Transition(DeploymentStatus.Pulling, Now.AddSeconds(1));
        d.StartedAt.Should().Be(Now.AddSeconds(1));
        d.Transition(DeploymentStatus.Starting, Now.AddSeconds(2));
        d.FinishedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(DeploymentStatus.Starting)]
    [InlineData(DeploymentStatus.Swapping)]
    [InlineData(DeploymentStatus.Completed)]
    public void Transition_rejects_invalid_jumps_from_pending(DeploymentStatus to)
    {
        var d = NewDeployment();

        var act = () => d.Transition(to, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_requires_a_new_container_id()
    {
        var d = AtSwapping(captureOld: false);

        var act = () => d.Complete(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_after_recording_new_container_succeeds_and_raises_events()
    {
        var d = AtSwapping(captureOld: false);
        d.RecordNewContainer("cont_new", Now);
        d.ClearDomainEvents();

        d.Complete(Now.AddSeconds(1));

        d.Status.Should().Be(DeploymentStatus.Completed);
        d.FinishedAt.Should().Be(Now.AddSeconds(1));
        d.DomainEvents.OfType<DeploymentStatusChangedDomainEvent>().Should().ContainSingle();
        d.DomainEvents.OfType<DeploymentCompletedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Fail_sets_failed_records_stage_and_is_idempotent_in_terminal()
    {
        var d = AtSwapping(captureOld: false);
        d.ClearDomainEvents();

        d.Fail("swap_failed", "yarp error", Now.AddSeconds(1));

        d.Status.Should().Be(DeploymentStatus.Failed);
        d.ErrorCode.Should().Be("swap_failed");
        d.FailedAtStage.Should().Be(DeploymentStatus.Swapping);

        d.ClearDomainEvents();
        d.Fail("other", "msg", Now.AddSeconds(2));
        d.ErrorCode.Should().Be("swap_failed");
        d.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Rollback_requires_failed_state()
    {
        var d = NewDeployment(); // Pending

        var act = () => d.Rollback(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rollback_is_blocked_when_no_old_container_was_captured()
    {
        var d = AtSwapping(captureOld: false);
        d.Fail("swap_failed", "yarp error", Now);

        var act = () => d.Rollback(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rollback_from_failed_with_old_container_succeeds()
    {
        var d = AtSwapping(captureOld: true);
        d.Fail("swap_failed", "yarp error", Now);
        d.ClearDomainEvents();

        d.Rollback(Now.AddSeconds(1));

        d.Status.Should().Be(DeploymentStatus.RolledBack);
        d.FinishedAt.Should().Be(Now.AddSeconds(1));
        d.DomainEvents.OfType<DeploymentStatusChangedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void RecordOldContainer_with_blank_leaves_it_null()
    {
        var d = NewDeployment();
        d.Transition(DeploymentStatus.Pulling, Now);

        d.RecordOldContainer("", "", Now);

        d.OldContainerId.Should().BeNull();
    }

    [Fact]
    public void Cancel_is_allowed_from_pending()
    {
        var d = NewDeployment();

        d.Cancel(Now);

        d.Status.Should().Be(DeploymentStatus.Cancelled);
        d.FinishedAt.Should().Be(Now);
    }

    [Fact]
    public void Cancel_is_rejected_from_starting()
    {
        var d = NewDeployment();
        d.Transition(DeploymentStatus.Pulling, Now);
        d.Transition(DeploymentStatus.Starting, Now);

        var act = () => d.Cancel(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsTerminal_reflects_status()
    {
        DeploymentStatus.RolledBack.IsTerminal().Should().BeTrue();
        DeploymentStatus.Completed.IsTerminal().Should().BeTrue();
        DeploymentStatus.Cancelled.IsTerminal().Should().BeTrue();
        DeploymentStatus.Pending.IsTerminal().Should().BeFalse();
        DeploymentStatus.Swapping.IsInProgress().Should().BeTrue();
    }
}
