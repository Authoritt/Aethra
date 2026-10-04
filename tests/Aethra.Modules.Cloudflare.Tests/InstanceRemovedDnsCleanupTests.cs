using System.Reflection;
using System.Text;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Proxy;
using Aethra.Shared.Kernel.Results;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aethra.Modules.Cloudflare.Tests;

/// <summary>
/// OT-009 — al borrar una instancia, el DNS de los hosts fijados por servicio (overrides) se borra
/// con ella; los records de hosts que NO son de la instancia no se tocan.
/// </summary>
public sealed class InstanceRemovedDnsCleanupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Borra_el_DNS_de_la_raiz_y_de_los_hosts_de_servicio_de_la_instancia()
    {
        await using var db = NewDb();
        var zone = CloudflareZone.Create("cf-zone", "inonetickets.lat", "acct", Encoding.UTF8.GetBytes("tok"), Now);
        db.Zones.Add(zone);
        var root = Cname(zone, "pruebas.inonetickets.lat");
        var api = Cname(zone, "pruebas-api.inonetickets.lat");
        var org = Cname(zone, "pruebas-org.inonetickets.lat");
        var prodApi = Cname(zone, "api.inonetickets.lat");
        db.DnsRecords.AddRange(root, api, org, prodApi);
        await db.SaveChangesAsync();
        var mediator = new RecordingMediator();

        await HandleAsync(db, mediator, new InstanceRemovedIntegrationEvent(
            "ins_stg", AutoHostname: null, CustomDomain: "pruebas.inonetickets.lat", RemovedAt: Now,
            ServiceHostnames: ["pruebas-api.inonetickets.lat", "pruebas-org.inonetickets.lat"]));

        mediator.Deleted.Should().BeEquivalentTo(
        [
            root.Id.ToString(), api.Id.ToString(), org.Id.ToString(),
        ]);
        mediator.Deleted.Should().NotContain(prodApi.Id.ToString());
    }

    [Fact]
    public async Task Evento_viejo_sin_hosts_de_servicio_borra_solo_lo_de_siempre()
    {
        await using var db = NewDb();
        var zone = CloudflareZone.Create("cf-zone", "inonetickets.lat", "acct", Encoding.UTF8.GetBytes("tok"), Now);
        db.Zones.Add(zone);
        var root = Cname(zone, "pruebas.inonetickets.lat");
        var api = Cname(zone, "pruebas-api.inonetickets.lat");
        db.DnsRecords.AddRange(root, api);
        await db.SaveChangesAsync();
        var mediator = new RecordingMediator();

        await HandleAsync(db, mediator, new InstanceRemovedIntegrationEvent(
            "ins_stg", AutoHostname: null, CustomDomain: "pruebas.inonetickets.lat", RemovedAt: Now));

        mediator.Deleted.Should().Equal(root.Id.ToString());
    }

    // ---- G2 de OT-009 (ALTO): el override de staging coincide con un host de producción ----

    [Fact]
    public async Task No_borra_el_DNS_de_un_override_que_otra_instancia_declara_como_suyo()
    {
        await using var db = NewDb();
        var zone = CloudflareZone.Create("cf-zone", "inonetickets.lat", "acct", Encoding.UTF8.GetBytes("tok"), Now);
        db.Zones.Add(zone);
        var prodApi = Cname(zone, "api.inonetickets.lat");
        db.DnsRecords.Add(prodApi);
        await db.SaveChangesAsync();
        var mediator = new RecordingMediator();

        await HandleAsync(db, mediator, StagingRemovedWithTypo(), new FakeRouteHosts(),
            new FakeUsage(("api.inonetickets.lat", "ins_prod")));

        mediator.Deleted.Should().NotContain(prodApi.Id.ToString());
    }

    [Fact]
    public async Task No_borra_el_DNS_de_un_override_que_sirve_una_ruta_de_otro_duenio()
    {
        await using var db = NewDb();
        var zone = CloudflareZone.Create("cf-zone", "inonetickets.lat", "acct", Encoding.UTF8.GetBytes("tok"), Now);
        db.Zones.Add(zone);
        var prodApi = Cname(zone, "api.inonetickets.lat");
        db.DnsRecords.Add(prodApi);
        await db.SaveChangesAsync();
        var mediator = new RecordingMediator();

        // prod no lo declara (viene de 'api.{domain}' del template), pero su ruta sí existe.
        await HandleAsync(db, mediator, StagingRemovedWithTypo(),
            new FakeRouteHosts(new RouteHostOwnerView("api.inonetickets.lat", "/", "ins_prod")), new FakeUsage());

        mediator.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Borra_el_DNS_del_override_si_solo_lo_sirven_rutas_de_la_propia_instancia()
    {
        await using var db = NewDb();
        var zone = CloudflareZone.Create("cf-zone", "inonetickets.lat", "acct", Encoding.UTF8.GetBytes("tok"), Now);
        db.Zones.Add(zone);
        var api = Cname(zone, "pruebas-api.inonetickets.lat");
        db.DnsRecords.Add(api);
        await db.SaveChangesAsync();
        var mediator = new RecordingMediator();

        await HandleAsync(db, mediator,
            new InstanceRemovedIntegrationEvent("ins_stg", null, null, Now,
                ServiceHostnames: ["pruebas-api.inonetickets.lat"]),
            new FakeRouteHosts(new RouteHostOwnerView("pruebas-api.inonetickets.lat", "/", "ins_stg")), new FakeUsage());

        mediator.Deleted.Should().Equal(api.Id.ToString());
    }

    private static InstanceRemovedIntegrationEvent StagingRemovedWithTypo()
        => new("ins_stg", AutoHostname: null, CustomDomain: "pruebas.inonetickets.lat", RemovedAt: Now,
            ServiceHostnames: ["api.inonetickets.lat"]);

    private sealed class FakeRouteHosts(params RouteHostOwnerView[] routes) : IRouteHostLookup
    {
        public Task<IReadOnlyList<RouteHostOwnerView>> FindByHostnameAsync(string hostname, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<RouteHostOwnerView>>(
                [.. routes.Where(r => string.Equals(r.Hostname, hostname, StringComparison.OrdinalIgnoreCase))]);
    }

    private sealed class FakeUsage(params (string Host, string InstanceId)[] uses) : IHostnameUsageLookup
    {
        public Task<IReadOnlyList<string>> FindInstancesUsingHostnameAsync(
            string hostname, string? excludingInstanceId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(
                [.. uses.Where(u => string.Equals(u.Host, hostname, StringComparison.OrdinalIgnoreCase)
                    && u.InstanceId != excludingInstanceId).Select(u => u.InstanceId)]);
    }

    private static DnsRecord Cname(CloudflareZone zone, string name)
        => DnsRecord.Create(zone.Id, DnsRecordType.CNAME, name, "tun.cfargotunnel.com", 1, true, null, Now);

    private static Task HandleAsync(CloudflareDbContext db, IMediator mediator, InstanceRemovedIntegrationEvent e)
        => HandleAsync(db, mediator, e, new FakeRouteHosts(), new FakeUsage());

    private static async Task HandleAsync(
        CloudflareDbContext db, IMediator mediator, InstanceRemovedIntegrationEvent e,
        IRouteHostLookup routes, IHostnameUsageLookup usage)
    {
        var type = typeof(CloudflareDbContext).Assembly
            .GetType("Aethra.Modules.Cloudflare.Infrastructure.Handlers.InstanceRemovedHandler", true)!;
        var logger = typeof(NullLogger<>).MakeGenericType(type)
            .GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        var handler = Activator.CreateInstance(
            type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [db, mediator, routes, usage, logger], null)!;
        await (Task)type.GetMethod("Handle")!.Invoke(handler, [e, CancellationToken.None])!;
    }

    private static CloudflareDbContext NewDb()
        => new(new DbContextOptionsBuilder<CloudflareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class RecordingMediator : IMediator
    {
        public List<string> Deleted { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is DeleteDnsRecordCommand delete)
            {
                Deleted.Add(delete.RecordId);
                return Task.FromResult((TResponse)(object)Result.Success());
            }
            throw new NotSupportedException(request.GetType().Name);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => throw new NotSupportedException();
    }
}
