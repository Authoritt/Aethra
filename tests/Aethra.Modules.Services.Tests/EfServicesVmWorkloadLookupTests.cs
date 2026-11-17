using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Lookups;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethra.Modules.Services.Tests;

public sealed class EfServicesVmWorkloadLookupTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ServicesDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ServicesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ServicesDbContext(options);
    }

    [Fact]
    public async Task ListOnVmAsync_returns_only_services_on_the_specified_vm()
    {
        await using var db = CreateDb();

        var svc1 = ManagedService.Adopt("redis", "Redis Main", ServiceType.Redis, "7", "vm_target_1",
            "redis-main", "redis:7-alpine", 6379, "aethra-net", [1], Now);

        var svc2 = ManagedService.Adopt("postgres", "Postgres Main", ServiceType.Postgres, "16", "vm_target_1",
            "postgres-main", "postgres:16-alpine", 5432, "aethra-net", [1], Now);

        var svc3 = ManagedService.Adopt("rabbit", "Rabbit Main", ServiceType.RabbitMQ, "3", "vm_target_2",
            "rabbit-main", "rabbitmq:3-management", 5672, "aethra-net", [1], Now);

        db.ManagedServices.AddRange(svc1, svc2, svc3);
        await db.SaveChangesAsync();

        var lookup = new EfServicesVmWorkloadLookup(db);

        var results = await lookup.ListOnVmAsync("vm_target_1", CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Kind == "managed_service");
        results.Select(r => r.Id).Should().BeEquivalentTo([svc1.Id.ToString(), svc2.Id.ToString()]);
        results.Select(r => r.Name).Should().BeEquivalentTo([svc1.Name, svc2.Name]);

        var otherResults = await lookup.ListOnVmAsync("vm_target_2", CancellationToken.None);
        otherResults.Should().ContainSingle();
        otherResults[0].Id.Should().Be(svc3.Id.ToString());
        otherResults[0].Name.Should().Be(svc3.Name);

        var emptyResults = await lookup.ListOnVmAsync("vm_none", CancellationToken.None);
        emptyResults.Should().BeEmpty();

        var blankResults = await lookup.ListOnVmAsync("", CancellationToken.None);
        blankResults.Should().BeEmpty();
    }
}
