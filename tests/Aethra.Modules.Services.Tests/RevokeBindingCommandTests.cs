using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Binding;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Modules.Services.UseCases.Commands;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Services;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

public sealed class RevokeBindingCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Revoke_succeeds_locally_when_reconciler_marked_service_container_missing()
    {
        await using var db = NewDb();
        var service = ManagedService.Adopt("redis", "Redis", ServiceType.Redis, "7", "vm_1", "redis",
            "redis:7-alpine", 6379, "aethra-net", [1], Now);
        service.MarkRuntimeContainerMissing(Now.AddMinutes(1));

        var binding = ServiceBinding.Create(service.Id, "inst_1", "app", [2],
            BindingPermissions.ReadWrite, null, null, Now);
        binding.MarkProvisioned(Now);

        db.ManagedServices.Add(service);
        db.ServiceBindings.Add(binding);
        await db.SaveChangesAsync();

        var provisioner = new RecordingProvisioner(ServiceType.Redis, RevokeOutcome: new(false, "redis.acl_failed", "UnableToConnect"));
        var envWriter = new RecordingEnvVarWriter();
        var handler = new RevokeBindingHandler(db, [provisioner], new StaticBindingCodec(),
            envWriter, new FixedClock(Now.AddMinutes(2)), NullLogger<RevokeBindingHandler>.Instance);

        var result = await handler.Handle(new RevokeBindingCommand(binding.Id.ToString()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        provisioner.RevokeCalls.Should().Be(0);
        envWriter.RemovedSources.Should().ContainSingle($"binding:{binding.Id}");
        (await db.ServiceBindings.SingleAsync()).RevokedAt.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public async Task Revoke_still_blocks_when_service_is_ready_and_remote_revoke_fails()
    {
        await using var db = NewDb();
        var service = ManagedService.Adopt("redis", "Redis", ServiceType.Redis, "7", "vm_1", "redis",
            "redis:7-alpine", 6379, "aethra-net", [1], Now);
        var binding = ServiceBinding.Create(service.Id, "inst_1", "app", [2],
            BindingPermissions.ReadWrite, null, null, Now);
        binding.MarkProvisioned(Now);

        db.ManagedServices.Add(service);
        db.ServiceBindings.Add(binding);
        await db.SaveChangesAsync();

        var provisioner = new RecordingProvisioner(ServiceType.Redis, RevokeOutcome: new(false, "redis.acl_failed", "UnableToConnect"));
        var handler = new RevokeBindingHandler(db, [provisioner], new StaticBindingCodec(),
            new RecordingEnvVarWriter(), new FixedClock(Now.AddMinutes(2)), NullLogger<RevokeBindingHandler>.Instance);

        var result = await handler.Handle(new RevokeBindingCommand(binding.Id.ToString()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        provisioner.RevokeCalls.Should().Be(1);
        (await db.ServiceBindings.SingleAsync()).RevokedAt.Should().BeNull();
    }

    private static ServicesDbContext NewDb()
        => new(new DbContextOptionsBuilder<ServicesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class StaticBindingCodec : IBindingCredentialsCodec
    {
        public byte[] Encode(BindingCredentials credentials) => [1];
        public BindingCredentials Decode(byte[] cipher) => new("app_user", "secret");
    }

    private sealed class RecordingEnvVarWriter : IEnvVarWriter
    {
        public List<string> RemovedSources { get; } = [];

        public Task UpsertManyAsync(EnvVarScope scope, string scopeId, string source, IReadOnlyList<EnvVarUpsert> vars, CancellationToken ct)
            => Task.CompletedTask;

        public Task RemoveBySourceAsync(EnvVarScope scope, string scopeId, string source, CancellationToken ct)
        {
            RemovedSources.Add(source);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProvisioner(ServiceType type, RevokeOutcome RevokeOutcome) : IServiceProvisioner
    {
        public int RevokeCalls { get; private set; }
        public ServiceType SupportedType => type;
        public Task<ProvisionOutcome> ProvisionAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
            => Task.FromResult(new ProvisionOutcome(true, null, null));
        public Task<RevokeOutcome> RevokeAsync(ManagedService service, ServiceBinding binding, BindingCredentials credentials, CancellationToken cancellationToken)
        {
            RevokeCalls++;
            return Task.FromResult(RevokeOutcome);
        }
        public Task<RotateOutcome> RotateAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
            => Task.FromResult(new RotateOutcome(true, null, null));
        public Task<TestOutcome> TestConnectionAsync(ManagedService service, CancellationToken cancellationToken)
            => Task.FromResult(new TestOutcome(true, "ok"));
    }
}
