using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Domain.Events;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Vms.Tests;

/// <summary>
/// Invariantes del agregado <see cref="Vm"/>: registro y máquina de estados de conectividad
/// (Pending → Connected ↔ Disconnected), promoción de <see cref="InstallStatus"/> al recibir
/// handshake, el rolling buffer de <see cref="Vm.InstallLog"/> y la idempotencia de
/// <see cref="Vm.SetAcceptsPreviews"/>.
/// </summary>
public sealed class VmTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Vm NewVm() => Vm.Register(Slug.Create("vm-1").Value, "VM One", Now).Vm;

    // ---------- Register ----------

    [Fact]
    public void Register_starts_pending_with_defaults_and_returns_a_token()
    {
        var (token, vm) = Vm.Register(Slug.Create("vm-1").Value, "  VM One  ", Now,
            publicIp: " 1.2.3.4 ", description: "  a host ");

        token.Should().NotBeNullOrWhiteSpace();
        vm.Status.Should().Be(VmStatus.Pending);
        vm.InstallStatus.Should().Be(InstallStatus.NotInstalled);
        vm.AcceptsPreviews.Should().BeTrue();
        vm.InstallLog.Should().BeEmpty();
        vm.Name.Should().Be("VM One");
        vm.PublicIp.Should().Be("1.2.3.4");
        vm.Description.Should().Be("a host");
        vm.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<VmRegisteredEvent>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_throws_on_blank_name(string name)
    {
        var act = () => Vm.Register(Slug.Create("vm-1").Value, name, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RotateToken_issues_a_different_token_and_raises_event()
    {
        var (original, vm) = Vm.Register(Slug.Create("vm-1").Value, "VM One", Now);
        vm.ClearDomainEvents();

        var rotated = vm.RotateToken(Now.AddMinutes(1));

        rotated.Should().NotBeNullOrWhiteSpace();
        rotated.Should().NotBe(original);
        vm.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SatelliteTokenRotatedEvent>();
    }

    // ---------- Connectivity state machine ----------

    [Fact]
    public void RecordConnected_sets_host_snapshot_and_connected_status()
    {
        var vm = NewVm();
        vm.ClearDomainEvents();

        vm.RecordConnected("host-a", "6.1.0", "ARM Neoverse", 4, 24_000_000_000, "1.0.0", Now,
            containerRuntime: " docker ", runtimeSocketAccessible: true, dataVolumePath: " /data ");

        vm.Status.Should().Be(VmStatus.Connected);
        vm.Hostname.Should().Be("host-a");
        vm.CpuCores.Should().Be(4);
        vm.ContainerRuntime.Should().Be("docker");
        vm.RuntimeSocketAccessible.Should().BeTrue();
        vm.DataVolumePath.Should().Be("/data");
        vm.LastConnectedAt.Should().Be(Now);
        vm.LastSeenAt.Should().Be(Now);
        vm.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SatelliteConnectedDomainEvent>();
    }

    [Fact]
    public void RecordConnected_promotes_not_installed_to_installed()
    {
        var vm = NewVm();

        vm.RecordConnected("h", "k", "c", 2, 1000, "1.0", Now);

        vm.InstallStatus.Should().Be(InstallStatus.Installed);
    }

    [Fact]
    public void RecordConnected_does_not_downgrade_a_failed_install()
    {
        var vm = NewVm();
        vm.MarkInstallFailed("ssh.timeout", "no response", Now);

        vm.RecordConnected("h", "k", "c", 2, 1000, "1.0", Now.AddMinutes(1));

        vm.InstallStatus.Should().Be(InstallStatus.Failed);
    }

    [Fact]
    public void RecordDisconnected_sets_disconnected_and_raises_event()
    {
        var vm = NewVm();
        vm.RecordConnected("h", "k", "c", 2, 1000, "1.0", Now);
        vm.ClearDomainEvents();

        vm.RecordDisconnected("network drop", Now.AddMinutes(1));

        vm.Status.Should().Be(VmStatus.Disconnected);
        vm.LastDisconnectedAt.Should().Be(Now.AddMinutes(1));
        vm.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SatelliteDisconnectedDomainEvent>();
    }

    [Fact]
    public void RecordHeartbeat_updates_last_seen_without_changing_status()
    {
        var vm = NewVm();

        vm.RecordHeartbeat(Now.AddMinutes(5));

        vm.LastSeenAt.Should().Be(Now.AddMinutes(5));
        vm.Status.Should().Be(VmStatus.Pending);
    }

    // ---------- Install lifecycle ----------

    [Fact]
    public void BeginInstall_sets_installing_and_logs_start()
    {
        var vm = NewVm();

        vm.BeginInstall(Now);

        vm.InstallStatus.Should().Be(InstallStatus.Installing);
        vm.InstallLog.Should().Contain("install_started");
    }

    [Fact]
    public void MarkInstallFailed_sets_failed_and_logs_the_error_code()
    {
        var vm = NewVm();

        vm.MarkInstallFailed("ssh.timeout", "no response", Now);

        vm.InstallStatus.Should().Be(InstallStatus.Failed);
        vm.InstallLog.Should().Contain("ssh.timeout");
    }

    // ---------- InstallLog rolling buffer ----------

    [Fact]
    public void AppendInstallLog_ignores_empty_lines()
    {
        var vm = NewVm();

        vm.AppendInstallLog("");

        vm.InstallLog.Should().BeEmpty();
    }

    [Fact]
    public void AppendInstallLog_normalizes_crlf_to_lf()
    {
        var vm = NewVm();

        vm.AppendInstallLog("a\r\nb");

        vm.InstallLog.Should().Be("a\nb");
    }

    [Fact]
    public void AppendInstallLog_keeps_only_the_last_max_lines_dropping_oldest()
    {
        var vm = NewVm();
        const int extra = 50;
        var total = Vm.MaxInstallLogLines + extra;
        for (var i = 1; i <= total; i++)
        {
            vm.AppendInstallLog($"line-{i}");
        }

        var lines = vm.InstallLog.Split('\n');
        lines.Should().HaveCount(Vm.MaxInstallLogLines);
        lines[^1].Should().Be($"line-{total}");
        lines[0].Should().Be($"line-{extra + 1}");
    }

    [Fact]
    public void AppendInstallLog_truncates_an_overlong_line()
    {
        var vm = NewVm();

        vm.AppendInstallLog(new string('x', 9000));

        vm.InstallLog.Should().EndWith("…[truncated]");
        vm.InstallLog.Length.Should().Be((8 * 1024) + "…[truncated]".Length);
    }

    // ---------- SetAcceptsPreviews ----------

    [Fact]
    public void SetAcceptsPreviews_changes_the_value()
    {
        var vm = NewVm();

        vm.SetAcceptsPreviews(false, Now.AddMinutes(1));

        vm.AcceptsPreviews.Should().BeFalse();
    }

    [Fact]
    public void SetAcceptsPreviews_is_idempotent_and_does_not_bump_updated_at()
    {
        var vm = NewVm();
        var before = vm.UpdatedAt;

        vm.SetAcceptsPreviews(true, Now.AddHours(1));

        vm.UpdatedAt.Should().Be(before, "un set idempotente no debe marcar el aggregate como modificado");
    }
}
