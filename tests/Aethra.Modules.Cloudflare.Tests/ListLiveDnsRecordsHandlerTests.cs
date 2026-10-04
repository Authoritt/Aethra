using System.Reflection;
using System.Text;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries;
using Aethra.Shared.Kernel.Results;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

/// <summary>
/// OT-008 T15 #1: la decision de DNS en Direct usa los records VIVOS de Cloudflare, no la copia local
/// (desfasada: medido 2026-10-04 con authoritforge.dev / inonetickets.lat).
/// </summary>
public sealed class ListLiveDnsRecordsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private const string Host = "app.inonetickets.lat";
    private const string Target = "tun-1.cfargotunnel.com";

    [Fact]
    public async Task Live_records_win_over_a_stale_local_copy()
    {
        await using var db = NewDb();
        var zone = CloudflareZone.Create("cf-zone", "inonetickets.lat", "acct", Encoding.UTF8.GetBytes("tok"), Now);
        db.Zones.Add(zone);
        // Copia local desfasada: no conoce el CNAME creado a mano en Cloudflare (caso 81053 de iot-prod).
        await db.SaveChangesAsync();
        var api = new LiveApi([new CloudflareDnsRecordInfo("r1", "CNAME", Host, Target, 1, true, null)]);

        var live = await ListAsync(db, api, zone.Id.ToString());

        live.IsSuccess.Should().BeTrue();
        api.ListedZones.Should().Equal("cf-zone");
        api.Tokens.Should().Equal("tok");
        DnsRecordRules.ClassifyHost(live.Value!, Host, Target).Action.Should().Be(DnsRecordRuleAction.Ok);
    }

    [Fact]
    public async Task Record_removed_in_cloudflare_but_present_locally_is_not_reported()
    {
        await using var db = NewDb();
        var zone = CloudflareZone.Create("cf-zone", "inonetickets.lat", "acct", Encoding.UTF8.GetBytes("tok"), Now);
        db.Zones.Add(zone);
        // smoke-bogota: borrado en Cloudflare, sigue en la copia local.
        db.DnsRecords.Add(DnsRecord.Create(zone.Id, DnsRecordType.CNAME, Host, "viejo.example.com", 1, true, null, Now));
        await db.SaveChangesAsync();

        var live = await ListAsync(db, new LiveApi([]), zone.Id.ToString());

        DnsRecordRules.ClassifyHost(live.Value!, Host, Target).Action.Should().Be(DnsRecordRuleAction.Create);
    }

    [Fact]
    public async Task Cloudflare_failure_is_an_explicit_error_not_a_cache_fallback()
    {
        await using var db = NewDb();
        var zone = CloudflareZone.Create("cf-zone", "inonetickets.lat", "acct", Encoding.UTF8.GetBytes("tok"), Now);
        db.Zones.Add(zone);
        db.DnsRecords.Add(DnsRecord.Create(zone.Id, DnsRecordType.CNAME, Host, Target, 1, true, null, Now));
        await db.SaveChangesAsync();

        var live = await ListAsync(db, new LiveApi([]) { Fail = true }, zone.Id.ToString());

        live.IsFailure.Should().BeTrue();
        live.Error.Code.Should().Be("cloudflare.api_error");
    }

    [Fact]
    public async Task Unknown_zone_is_not_found()
    {
        await using var db = NewDb();
        var live = await ListAsync(db, new LiveApi([]), CloudflareZoneId.New().ToString());
        live.IsFailure.Should().BeTrue();
        live.Error.Code.Should().Be("cloudflare.zone_not_found");
    }

    private static async Task<Result<IReadOnlyList<CloudflareDnsRecordInfo>>> ListAsync(
        CloudflareDbContext db, LiveApi api, string zoneId)
    {
        var type = typeof(ListLiveDnsRecordsQuery).Assembly
            .GetType("Aethra.Modules.Cloudflare.UseCases.DnsRecords.Queries.ListLiveDnsRecordsHandler", true)!;
        var handler = Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [db, api, new PlainCodec()],
            null)!;
        return await (Task<Result<IReadOnlyList<CloudflareDnsRecordInfo>>>)type.GetMethod("Handle")!
            .Invoke(handler, [new ListLiveDnsRecordsQuery(zoneId), CancellationToken.None])!;
    }

    private static CloudflareDbContext NewDb()
        => new(new DbContextOptionsBuilder<CloudflareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class PlainCodec : ICloudflareTokenCodec
    {
        public byte[] Encode(string token) => Encoding.UTF8.GetBytes(token);

        public string Decode(byte[] cipher) => Encoding.UTF8.GetString(cipher);
    }

    private sealed class LiveApi(IReadOnlyList<CloudflareDnsRecordInfo> records) : ICloudflareApiClient
    {
        public bool Fail { get; init; }

        public List<string> ListedZones { get; } = [];

        public List<string> Tokens { get; } = [];

        public Task<IReadOnlyList<CloudflareDnsRecordInfo>> ListDnsRecordsAsync(
            string zoneId, string apiToken, CancellationToken cancellationToken)
        {
            ListedZones.Add(zoneId);
            Tokens.Add(apiToken);
            return Fail
                ? throw new CloudflareApiException(500, 0, "boom")
                : Task.FromResult(records);
        }

        public Task<CloudflareZoneInfo> GetZoneAsync(string z, string t, CancellationToken c) => throw new NotSupportedException();

        public Task<string> CreateDnsRecordAsync(string z, string t, CreateDnsRecordRequest r, CancellationToken c) => throw new NotSupportedException();

        public Task UpdateDnsRecordAsync(string z, string e, string t, UpdateDnsRecordRequest r, CancellationToken c) => throw new NotSupportedException();

        public Task DeleteDnsRecordAsync(string z, string e, string t, CancellationToken c) => throw new NotSupportedException();

        public Task<IReadOnlyList<TunnelIngressRule>> GetTunnelIngressAsync(string a, string t, string k, CancellationToken c) => throw new NotSupportedException();

        public Task PutTunnelIngressAsync(string a, string t, string k, IReadOnlyList<TunnelIngressRule> i, CancellationToken c) => throw new NotSupportedException();

        public Task<string> GetTunnelConnectorTokenAsync(string a, string t, string k, CancellationToken c) => throw new NotSupportedException();
    }
}
