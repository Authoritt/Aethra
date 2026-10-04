using System.Reflection;
using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Proxy.Infrastructure.Yarp;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aethra.Modules.Proxy.Tests;

/// <summary>
/// G2 de OT-009 (ALTO) — borrar una instancia cuyo override coincide con el host de OTRA (typo
/// <c>api.inonetickets.lat</c> en staging) no puede tocar las rutas de la otra: las rutas de override
/// se borran solo por dueño (<c>OperationalOwnerId</c>), nunca por hostname.
/// </summary>
public sealed class InstanceRemovedRoutesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Borrar_staging_con_override_igual_al_host_de_prod_no_toca_las_rutas_de_prod()
    {
        await using var db = NewDb();
        var prodApi = Owned("api.inonetickets.lat", "http://iot-prod-api:5091", "ins_prod");
        var stgRoot = Owned("pruebas.inonetickets.lat", "http://iot-stg-publica:3000", "ins_stg");
        var stgOrg = Owned("pruebas-org.inonetickets.lat", "http://iot-stg-org:3001", "ins_stg");
        db.Routes.AddRange(prodApi, stgRoot, stgOrg);
        await db.SaveChangesAsync();

        await HandleAsync(db, new InstanceRemovedIntegrationEvent(
            "ins_stg", AutoHostname: null, CustomDomain: "pruebas.inonetickets.lat", RemovedAt: Now,
            ServiceHostnames: ["api.inonetickets.lat", "pruebas-org.inonetickets.lat"]));

        var left = await db.Routes.AsNoTracking().ToListAsync();
        left.Select(r => r.Hostname.Value).Should().Equal("api.inonetickets.lat");
        left.Single().OperationalOwnerId.Should().Be("ins_prod");
    }

    private static Route Owned(string host, string backend, string owner)
    {
        var route = Route.Create(Hostname.Create(host).Value, "/", backend, false, Now);
        route.SetOperationalOwner("app_environment", owner, "native-deploy", Now);
        return route;
    }

    private static async Task HandleAsync(ProxyDbContext db, InstanceRemovedIntegrationEvent e)
    {
        var type = typeof(ProxyDbContext).Assembly
            .GetType("Aethra.Modules.Proxy.Infrastructure.Handlers.InstanceRemovedHandler", true)!;
        var logger = typeof(NullLogger<>).MakeGenericType(type)
            .GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        var handler = Activator.CreateInstance(
            type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [db, new NoReload(), logger], null)!;
        await (Task)type.GetMethod("Handle")!.Invoke(handler, [e, CancellationToken.None])!;
    }

    private static ProxyDbContext NewDb()
        => new(new DbContextOptionsBuilder<ProxyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class NoReload : IProxyConfigService
    {
        public void Reload()
        {
        }
    }
}
