using Aethra.Modules.Vms.Domain;
using Aethra.Modules.Vms.Infrastructure;
using Aethra.Modules.Vms.UseCases.Vms.Commands;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Contracts.Vms;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethra.Modules.Vms.Tests;

public sealed class DeleteVmHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static VmsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<VmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new VmsDbContext(options);
    }

    private static Vm SeedVm(VmsDbContext db, string name = "vm-test")
    {
        var (_, vm) = Vm.Register(Slug.Create(name).Value, name, Now, publicIp: "10.0.0.1");
        db.Vms.Add(vm);
        db.SaveChanges();
        return vm;
    }

    [Fact]
    public async Task Handle_with_workloads_and_without_force_returns_vm_in_use_and_keeps_vm_in_db()
    {
        await using var db = CreateDb();
        var vm = SeedVm(db);
        var vmId = vm.Id.ToString();

        var lookup = new FakeWorkloadLookup(new VmWorkloadRef("instance", "ins_1", "web-prod"));
        var registry = new FakeSatelliteRegistry(isConnected: false);
        var handler = new DeleteVmHandler(db, [lookup], registry);

        var result = await handler.Handle(new DeleteVmCommand(vmId, Force: false), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("vm.in_use");
        result.Error.Message.Should().Contain("web-prod");
        result.Error.Message.Should().Contain("instance:web-prod");

        (await db.Vms.AnyAsync(v => v.Id == vm.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_with_workloads_and_with_force_returns_vm_in_use_and_keeps_vm_in_db()
    {
        // Fuerza no omite la protección de cargas activas
        await using var db = CreateDb();
        var vm = SeedVm(db);
        var vmId = vm.Id.ToString();

        var lookup = new FakeWorkloadLookup(new VmWorkloadRef("managed_service", "svc_1", "postgres-main"));
        var registry = new FakeSatelliteRegistry(isConnected: true);
        var handler = new DeleteVmHandler(db, [lookup], registry);

        var result = await handler.Handle(new DeleteVmCommand(vmId, Force: true), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("vm.in_use");
        result.Error.Message.Should().Contain("postgres-main");

        (await db.Vms.AnyAsync(v => v.Id == vm.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_without_workloads_connected_satellite_and_without_force_returns_vm_satellite_connected()
    {
        await using var db = CreateDb();
        var vm = SeedVm(db);
        var vmId = vm.Id.ToString();

        var lookup = new FakeWorkloadLookup();
        var registry = new FakeSatelliteRegistry(isConnected: true);
        var handler = new DeleteVmHandler(db, [lookup], registry);

        var result = await handler.Handle(new DeleteVmCommand(vmId, Force: false), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("vm.satellite_connected");
        result.Error.Message.Should().Contain("force=true");

        (await db.Vms.AnyAsync(v => v.Id == vm.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_without_workloads_connected_satellite_and_with_force_deletes_vm()
    {
        await using var db = CreateDb();
        var vm = SeedVm(db);
        var vmId = vm.Id.ToString();

        var lookup = new FakeWorkloadLookup();
        var registry = new FakeSatelliteRegistry(isConnected: true);
        var handler = new DeleteVmHandler(db, [lookup], registry);

        var result = await handler.Handle(new DeleteVmCommand(vmId, Force: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await db.Vms.AnyAsync(v => v.Id == vm.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_without_workloads_disconnected_satellite_and_without_force_deletes_vm()
    {
        await using var db = CreateDb();
        var vm = SeedVm(db);
        var vmId = vm.Id.ToString();

        var lookup = new FakeWorkloadLookup();
        var registry = new FakeSatelliteRegistry(isConnected: false);
        var handler = new DeleteVmHandler(db, [lookup], registry);

        var result = await handler.Handle(new DeleteVmCommand(vmId, Force: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await db.Vms.AnyAsync(v => v.Id == vm.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_with_more_than_ten_workloads_truncates_summary_with_remaining_count()
    {
        await using var db = CreateDb();
        var vm = SeedVm(db);
        var vmId = vm.Id.ToString();

        var items = Enumerable.Range(1, 13)
            .Select(i => new VmWorkloadRef("instance", $"ins_{i}", $"app-{i}"))
            .ToArray();

        var lookup = new FakeWorkloadLookup(items);
        var handler = new DeleteVmHandler(db, [lookup], new FakeSatelliteRegistry());

        var result = await handler.Handle(new DeleteVmCommand(vmId, Force: false), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("vm.in_use");
        result.Error.Message.Should().Contain("13 carga(s) asignada(s)");
        result.Error.Message.Should().Contain("app-1");
        result.Error.Message.Should().Contain("app-10");
        result.Error.Message.Should().Contain("y 3 más");
    }

    [Fact]
    public async Task Handle_with_invalid_id_returns_validation_error()
    {
        await using var db = CreateDb();
        var handler = new DeleteVmHandler(db);

        var result = await handler.Handle(new DeleteVmCommand("not-a-vm-id", Force: false), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("vm.invalid_id");
    }

    [Fact]
    public async Task Handle_when_vm_not_found_returns_not_found()
    {
        await using var db = CreateDb();
        var handler = new DeleteVmHandler(db);

        var nonExistentId = Aethra.Shared.Kernel.Ids.AethraId.NewId("vm").ToString();
        var result = await handler.Handle(new DeleteVmCommand(nonExistentId, Force: false), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("vm.not_found");
    }

    private sealed class FakeWorkloadLookup(params VmWorkloadRef[] workloads) : IVmWorkloadLookup
    {
        public Task<IReadOnlyList<VmWorkloadRef>> ListOnVmAsync(string vmId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<VmWorkloadRef>>(workloads);
    }

    private sealed class FakeSatelliteRegistry(bool isConnected = false) : ISatelliteConnectionRegistry
    {
        public void Register(string vmId, string connectionId) { }
        public void Unregister(string vmId, string connectionId) { }
        public bool IsConnected(string vmId) => isConnected;
        public string? GetConnectionId(string vmId) => isConnected ? "conn_1" : null;
        public IReadOnlyCollection<string> ConnectedVmIds => isConnected ? ["vm_test"] : [];
    }
}
