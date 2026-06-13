using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.Domain.Build.Events;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// Invariantes del agregado <see cref="Build"/>: la state machine del pipeline
/// (Queued→Cloning→Building→Pushing→Completed, transiciones inválidas lanzan), StartedAt/FinishedAt,
/// Complete (requiere ImageRef), Fail (registra stage + idempotente en terminal), Cancel (solo
/// estados tempranos) y el log con sequence monotónico.
/// </summary>
public sealed class BuildTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Build NewBuild()
        => Build.Queue("tpl_1", "ABCDEF1234567890", "refs/heads/main", BuildTrigger.Webhook, null, Now);

    [Fact]
    public void Queue_starts_queued_lowercases_sha_logs_and_raises_event()
    {
        var build = Build.Queue("tpl_1", "ABCDEF1", "refs/heads/main", BuildTrigger.Manual, "user@x", Now);

        build.Status.Should().Be(BuildStatus.Queued);
        build.GitSha.Should().Be("abcdef1");
        build.GitRef.Should().Be("refs/heads/main");
        build.Logs.Should().ContainSingle();
        build.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<BuildQueuedEvent>();
    }

    [Theory]
    [InlineData("", "sha", "refs/heads/main")]
    [InlineData("tpl", "", "refs/heads/main")]
    [InlineData("tpl", "sha", "")]
    public void Queue_throws_on_blank_required_fields(string templateId, string sha, string gitRef)
    {
        var act = () => Build.Queue(templateId, sha, gitRef, BuildTrigger.Webhook, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Transition_follows_the_happy_path_and_sets_started_at()
    {
        var build = NewBuild();

        build.Transition(BuildStatus.Cloning, Now.AddSeconds(1));
        build.Status.Should().Be(BuildStatus.Cloning);
        build.StartedAt.Should().Be(Now.AddSeconds(1));

        build.Transition(BuildStatus.Building, Now.AddSeconds(2));
        build.Transition(BuildStatus.Pushing, Now.AddSeconds(3));
        build.FinishedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(BuildStatus.Building)]
    [InlineData(BuildStatus.Pushing)]
    [InlineData(BuildStatus.Completed)]
    public void Transition_rejects_invalid_jumps_from_queued(BuildStatus to)
    {
        var build = NewBuild();

        var act = () => build.Transition(to, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_requires_an_image_ref()
    {
        var build = NewBuild();
        build.Transition(BuildStatus.Cloning, Now);
        build.Transition(BuildStatus.Building, Now);
        build.Transition(BuildStatus.Pushing, Now);

        var act = () => build.Complete(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_after_recording_image_ref_succeeds_and_raises_events()
    {
        var build = NewBuild();
        build.Transition(BuildStatus.Cloning, Now);
        build.Transition(BuildStatus.Building, Now);
        build.Transition(BuildStatus.Pushing, Now);
        build.RecordImageRef("registry/app:abc", 1234, Now);
        build.ClearDomainEvents();

        build.Complete(Now.AddSeconds(1));

        build.Status.Should().Be(BuildStatus.Completed);
        build.ImageRef.Should().Be("registry/app:abc");
        build.BuildDurationMs.Should().Be(1234);
        build.FinishedAt.Should().Be(Now.AddSeconds(1));
        build.DomainEvents.OfType<BuildStatusChangedDomainEvent>().Should().ContainSingle();
        build.DomainEvents.OfType<BuildCompletedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Fail_sets_failed_records_stage_and_is_idempotent_in_terminal()
    {
        var build = NewBuild();
        build.Transition(BuildStatus.Cloning, Now);
        build.ClearDomainEvents();

        build.Fail("clone_failed", "git error", Now.AddSeconds(1), durationMs: 500);

        build.Status.Should().Be(BuildStatus.Failed);
        build.ErrorCode.Should().Be("clone_failed");
        build.FailedAtStage.Should().Be(BuildStatus.Cloning);
        build.BuildDurationMs.Should().Be(500);
        build.FinishedAt.Should().Be(Now.AddSeconds(1));

        build.ClearDomainEvents();
        build.Fail("other", "msg", Now.AddSeconds(2)); // ya terminal → no-op
        build.ErrorCode.Should().Be("clone_failed");
        build.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Cancel_is_allowed_from_an_early_state()
    {
        var build = NewBuild(); // Queued

        build.Cancel(Now);

        build.Status.Should().Be(BuildStatus.Cancelled);
        build.FinishedAt.Should().Be(Now);
    }

    [Fact]
    public void Cancel_is_rejected_from_pushing()
    {
        var build = NewBuild();
        build.Transition(BuildStatus.Cloning, Now);
        build.Transition(BuildStatus.Building, Now);
        build.Transition(BuildStatus.Pushing, Now);

        var act = () => build.Cancel(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AppendLog_assigns_monotonic_unique_sequences()
    {
        var build = NewBuild(); // log inicial seq 0

        build.AppendLog(BuildLogLevel.Info, "stage", "line1", Now);
        build.AppendLog(BuildLogLevel.Warn, "stage", "line2", Now);

        var sequences = build.Logs.Select(l => l.Sequence).ToList();
        sequences.Should().BeInAscendingOrder();
        sequences.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void IsTerminal_and_IsInProgress_reflect_status()
    {
        BuildStatus.Completed.IsTerminal().Should().BeTrue();
        BuildStatus.Failed.IsTerminal().Should().BeTrue();
        BuildStatus.Cancelled.IsTerminal().Should().BeTrue();
        BuildStatus.Queued.IsTerminal().Should().BeFalse();
        BuildStatus.Building.IsInProgress().Should().BeTrue();
    }
}
