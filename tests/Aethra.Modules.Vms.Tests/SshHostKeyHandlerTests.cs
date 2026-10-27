using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.Infrastructure.Provisioning;
using Aethra.Modules.Vms.Infrastructure.Security;
using Aethra.Modules.Vms.UseCases.Vms.Commands;
using Aethra.Modules.Vms.UseCases.Vms.Queries;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aethra.Modules.Vms.Tests;

public sealed class SshHostKeyHandlerTests
{
    private const string PinnedFp = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";
    private const string NewRotatedFp = "SHA256:uD34F12345678901234567890123456789012345678";
    private const string DifferentFp = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);
    private readonly FakeJobQueue _queue = new();
    private readonly FakeCodec _codec = new();
    private readonly IConfiguration _config = new ConfigurationBuilder().Build();

    private static VmsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<VmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new VmsDbContext(options);
    }

    private static Vm SeedVm(VmsDbContext db, string name, string? pinnedFp = null, bool withSavedCreds = false)
    {
        var (token, vm) = Vm.Register(Slug.Create(name.ToLowerInvariant()).Value, name, DateTimeOffset.UtcNow,
            publicIp: "10.0.0.5");
        if (pinnedFp is not null)
        {
            vm.SetSshHostKeyFingerprint(pinnedFp, DateTimeOffset.UtcNow);
        }
        if (withSavedCreds)
        {
            vm.SetSshCredentials([1, 2, 3], DateTimeOffset.UtcNow);
        }
        db.Vms.Add(vm);
        db.SaveChanges();
        return vm;
    }

    [Fact]
    public async Task AutoInstall_without_expected_host_key_on_unpinned_vm_fails_with_validation_error()
    {
        using var db = CreateDb();
        var vm = SeedVm(db, "unpinned-vm", pinnedFp: null);

        var handler = new AutoInstallSatelliteHandler(db, _clock, _codec, _queue, _config);
        var creds = new SshCredentialsInput("10.0.0.5", 22, "root", "password", "pass123");
        var cmd = new AutoInstallSatelliteCommand(vm.Id.ToString(), creds, false, "docker");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ssh.host_key_required");
        _queue.EnqueuedJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task AutoInstall_with_expected_host_key_on_unpinned_vm_succeeds_and_enqueues_job()
    {
        using var db = CreateDb();
        var vm = SeedVm(db, "unpinned-vm-2", pinnedFp: null);

        var handler = new AutoInstallSatelliteHandler(db, _clock, _codec, _queue, _config);
        var creds = new SshCredentialsInput("10.0.0.5", 22, "root", "password", "pass123");
        var cmd = new AutoInstallSatelliteCommand(
            VmId: vm.Id.ToString(),
            Credentials: creds,
            InstallContainerRuntime: false,
            ContainerRuntime: "docker",
            DryRun: false,
            ExpectedHostKeyFingerprint: PinnedFp);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Installing");

        _queue.EnqueuedJobs.Should().ContainSingle();
        _queue.EnqueuedJobs[0].Options.ExpectedHostKeyFingerprint.Should().Be(PinnedFp);
    }

    [Fact]
    public async Task AutoInstall_with_mismatched_key_on_pinned_vm_without_rotate_flag_fails_with_conflict()
    {
        using var db = CreateDb();
        var vm = SeedVm(db, "pinned-vm", pinnedFp: PinnedFp);

        var handler = new AutoInstallSatelliteHandler(db, _clock, _codec, _queue, _config);
        var creds = new SshCredentialsInput("10.0.0.5", 22, "root", "password", "pass123");
        var cmd = new AutoInstallSatelliteCommand(
            VmId: vm.Id.ToString(),
            Credentials: creds,
            InstallContainerRuntime: false,
            ContainerRuntime: "docker",
            DryRun: false,
            ExpectedHostKeyFingerprint: DifferentFp,
            RotateHostKey: false);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ssh.host_key_mismatch");
        _queue.EnqueuedJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task AutoInstall_with_mismatched_key_on_pinned_vm_with_rotate_flag_succeeds_and_enqueues_with_new_key()
    {
        using var db = CreateDb();
        var vm = SeedVm(db, "pinned-vm-rotate", pinnedFp: PinnedFp);

        var handler = new AutoInstallSatelliteHandler(db, _clock, _codec, _queue, _config);
        var creds = new SshCredentialsInput("10.0.0.5", 22, "root", "password", "pass123");
        var cmd = new AutoInstallSatelliteCommand(
            VmId: vm.Id.ToString(),
            Credentials: creds,
            InstallContainerRuntime: false,
            ContainerRuntime: "docker",
            DryRun: false,
            ExpectedHostKeyFingerprint: NewRotatedFp,
            RotateHostKey: true);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _queue.EnqueuedJobs.Should().ContainSingle();
        _queue.EnqueuedJobs[0].Options.ExpectedHostKeyFingerprint.Should().Be(NewRotatedFp);
    }

    [Fact]
    public async Task Reinstall_fails_if_vm_has_no_pinned_host_key()
    {
        using var db = CreateDb();
        var vm = SeedVm(db, "reinstall-unpinned", pinnedFp: null, withSavedCreds: true);

        var handler = new ReinstallSatelliteHandler(db, _clock, _codec, _queue, _config);
        var cmd = new ReinstallSatelliteCommand(vm.Id.ToString(), false, "docker");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ssh.host_key_required");
        _queue.EnqueuedJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Reinstall_succeeds_and_uses_pinned_host_key()
    {
        using var db = CreateDb();
        var vm = SeedVm(db, "reinstall-pinned", pinnedFp: PinnedFp, withSavedCreds: true);

        var handler = new ReinstallSatelliteHandler(db, _clock, _codec, _queue, _config);
        var cmd = new ReinstallSatelliteCommand(vm.Id.ToString(), false, "docker");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _queue.EnqueuedJobs.Should().ContainSingle();
        _queue.EnqueuedJobs[0].Options.ExpectedHostKeyFingerprint.Should().Be(PinnedFp);
    }

    [Fact]
    public async Task RotateVmHostKey_updates_pinned_fingerprint_and_audit_log()
    {
        using var db = CreateDb();
        var vm = SeedVm(db, "vm-rotate", pinnedFp: PinnedFp);

        var handler = new RotateVmHostKeyHandler(db, _clock);
        var cmd = new RotateVmHostKeyCommand(vm.Id.ToString(), NewRotatedFp);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SshHostKeyFingerprint.Should().Be(NewRotatedFp);

        var updatedVm = await db.Vms.FindAsync(vm.Id);
        updatedVm!.SshHostKeyFingerprint.Should().Be(NewRotatedFp);
        updatedVm.InstallLog.Should().Contain("host_key_rotated");
    }

    [Fact]
    public async Task ProbeVmHostKey_returns_fingerprint_and_matches_pinned_key()
    {
        using var db = CreateDb();
        var vm = SeedVm(db, "vm-probe", pinnedFp: PinnedFp);

        var fakeProber = new FakeProber(new SshHostKeyProbeResult(true, "ssh-ed25519", PinnedFp, null, null));
        var handler = new ProbeVmHostKeyHandler(db, fakeProber, _codec);

        var query = new ProbeVmHostKeyQuery(VmId: vm.Id.ToString());
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.Fingerprint.Should().Be(PinnedFp);
        result.Value.MatchesPinned.Should().BeTrue();
        result.Value.PinnedFingerprint.Should().Be(PinnedFp);
    }

    [Fact]
    public async Task ProbeVmHostKey_detects_mismatch_against_pinned_key()
    {
        using var db = CreateDb();
        var vm = SeedVm(db, "vm-probe-mismatch", pinnedFp: PinnedFp);

        var fakeProber = new FakeProber(new SshHostKeyProbeResult(true, "ssh-ed25519", DifferentFp, null, null));
        var handler = new ProbeVmHostKeyHandler(db, fakeProber, _codec);

        var query = new ProbeVmHostKeyQuery(VmId: vm.Id.ToString());
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.Fingerprint.Should().Be(DifferentFp);
        result.Value.MatchesPinned.Should().BeFalse();
        result.Value.PinnedFingerprint.Should().Be(PinnedFp);
    }

    // =========================================================================
    // FAKES
    // =========================================================================

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeJobQueue : IInstallationJobQueue
    {
        public List<InstallationJob> EnqueuedJobs { get; } = [];

        public ValueTask EnqueueAsync(InstallationJob job, CancellationToken cancellationToken)
        {
            EnqueuedJobs.Add(job);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<InstallationJob> ReadAllAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private sealed class FakeCodec : ISshCredentialsCodec
    {
        public byte[] Encode(SshCredentials creds) => [1, 2, 3];
        public SshCredentials Decode(byte[] cipher) => new("10.0.0.5", 22, "root", SshAuthMethod.Password, "pass123");
    }

    private sealed class FakeProber(SshHostKeyProbeResult result) : ISshHostKeyProber
    {
        public Task<SshHostKeyProbeResult> ProbeAsync(string host, int port, CancellationToken ct)
            => Task.FromResult(result);
    }
}
