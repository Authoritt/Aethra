using System.Text;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Lookups;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

public sealed class EfCloudflareVmWorkloadLookupTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CloudflareDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CloudflareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CloudflareDbContext(options);
    }

    [Fact]
    public async Task ListOnVmAsync_returns_only_tunnels_on_the_specified_vm()
    {
        await using var db = CreateDb();

        var t1 = CloudflareTunnel.Create(
            "11111111-1111-1111-1111-111111111111",
            "authorit-apps",
            "0123456789abcdef0123456789abcdef",
            Encoding.UTF8.GetBytes("tok1"),
            null,
            null,
            false,
            Now,
            TunnelMode.CentralProxy,
            "vm_target_1");

        var t2 = CloudflareTunnel.Create(
            "22222222-2222-2222-2222-222222222222",
            "authorit-direct",
            "0123456789abcdef0123456789abcdef",
            Encoding.UTF8.GetBytes("tok2"),
            null,
            null,
            false,
            Now,
            TunnelMode.Direct,
            "vm_target_2");

        var t3 = CloudflareTunnel.Create(
            "33333333-3333-3333-3333-333333333333",
            "authorit-unassigned",
            "0123456789abcdef0123456789abcdef",
            Encoding.UTF8.GetBytes("tok3"),
            null,
            null,
            false,
            Now,
            TunnelMode.CentralProxy,
            null);

        db.Tunnels.AddRange(t1, t2, t3);
        await db.SaveChangesAsync();

        var lookup = new EfCloudflareVmWorkloadLookup(db);

        var results = await lookup.ListOnVmAsync("vm_target_1", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Kind.Should().Be("tunnel");
        results[0].Id.Should().Be(t1.Id.ToString());
        results[0].Name.Should().Be(t1.Name);

        var resultsVm2 = await lookup.ListOnVmAsync("vm_target_2", CancellationToken.None);
        resultsVm2.Should().ContainSingle();
        resultsVm2[0].Id.Should().Be(t2.Id.ToString());

        var emptyResults = await lookup.ListOnVmAsync("vm_none", CancellationToken.None);
        emptyResults.Should().BeEmpty();

        var blankResults = await lookup.ListOnVmAsync("", CancellationToken.None);
        blankResults.Should().BeEmpty();
    }
}
