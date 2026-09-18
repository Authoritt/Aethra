using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Reconciliation;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

public sealed class ManagedServiceRuntimeReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Marks_ready_service_failed_when_container_is_missing_on_target_vm()
    {
        await using var db = NewDb();
        var service = ManagedService.Adopt("redis", "Redis", ServiceType.Redis, "7", "vm_1", "redis",
            "redis:7-alpine", 6379, "aethra-net", [1], Now);
        db.ManagedServices.Add(service);
        await db.SaveChangesAsync();

        var reconciler = new ManagedServiceRuntimeReconciler(db, new StaticSatellite([]),
            new FixedClock(Now.AddMinutes(1)), NullLogger<ManagedServiceRuntimeReconciler>.Instance);

        var changed = await reconciler.ReconcileReadyServicesAsync(CancellationToken.None);

        changed.Should().Be(1);
        var saved = await db.ManagedServices.SingleAsync();
        saved.Status.Should().Be(ManagedServiceStatus.Failed);
        saved.ErrorCode.Should().Be(ManagedService.RuntimeContainerMissingErrorCode);
    }

    [Fact]
    public async Task Leaves_ready_service_ready_when_container_is_present()
    {
        await using var db = NewDb();
        var service = ManagedService.Adopt("redis", "Redis", ServiceType.Redis, "7", "vm_1", "redis",
            "redis:7-alpine", 6379, "aethra-net", [1], Now);
        db.ManagedServices.Add(service);
        await db.SaveChangesAsync();

        var containers = new[]
        {
            new ContainerInfo("id", "redis", "redis:7-alpine", "running", [6379]),
        };
        var reconciler = new ManagedServiceRuntimeReconciler(db, new StaticSatellite(containers),
            new FixedClock(Now.AddMinutes(1)), NullLogger<ManagedServiceRuntimeReconciler>.Instance);

        var changed = await reconciler.ReconcileReadyServicesAsync(CancellationToken.None);

        changed.Should().Be(0);
        (await db.ManagedServices.SingleAsync()).Status.Should().Be(ManagedServiceStatus.Ready);
    }

    private static ServicesDbContext NewDb()
        => new(new DbContextOptionsBuilder<ServicesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class StaticSatellite(IReadOnlyList<ContainerInfo> containers) : ISatelliteRpcClient
    {
        public Task<IReadOnlyList<ContainerInfo>> SendListContainersAsync(string vmId, CancellationToken ct)
            => Task.FromResult(containers);

        public Task<BuildResult> SendBuildAsync(string vmId, BuildSpec spec, RegistryAuth? pushTo, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<RunResult> SendRunAsync(string vmId, RunSpec spec, RegistryAuth? pullFrom, CancellationToken ct)
            => throw new NotSupportedException();
        public Task SendStopAsync(string vmId, string containerNameOrId, CancellationToken ct)
            => throw new NotSupportedException();
        public Task SendRestartAsync(string vmId, string containerNameOrId, CancellationToken ct)
            => throw new NotSupportedException();
        public Task SendRemoveAsync(string vmId, string containerNameOrId, bool force, CancellationToken ct)
            => throw new NotSupportedException();
        public IAsyncEnumerable<LogChunk> StreamLogsAsync(string vmId, string containerNameOrId, int tailLines, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<ExecResult> SendExecAsync(string vmId, string containerNameOrId, string command, int timeoutSeconds, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<StoreFileResponse> SendStoreFileAsync(string vmId, string relativePath, byte[] content, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<byte[]> SendReadFileAsync(string vmId, string relativePath, CancellationToken ct)
            => throw new NotSupportedException();
        public Task SendDeleteFileAsync(string vmId, string relativePath, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
