using Aethra.Modules.Projects.Domain.Clients;
using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.Infrastructure.Lookups;
using Aethra.Modules.Projects.UseCases.Instances.Commands;
using Aethra.Modules.Projects.UseCases.Instances.Queries;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Proxy;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

/// <summary>
/// OT-009 — override de hostname por (instancia, servicio): staging de InOneTickets publica
/// <c>api</c>/<c>org</c>/<c>admin</c> en <c>pruebas-*.inonetickets.lat</c> (un nivel, dentro del
/// certificado Universal) en vez de <c>api.pruebas.inonetickets.lat</c>.
/// </summary>
public sealed class SetServiceHostnameHandlerTests(ProjectsDbFixture fixture)
    : IClassFixture<ProjectsDbFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fija_los_tres_hosts_de_staging_persiste_y_encola_la_reconciliacion()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var outbox = new RecordingOutbox();
        var handler = NewHandler(db, outbox);

        foreach (var (svc, host) in StagingHosts)
        {
            var r = await handler.Handle(
                new SetServiceHostnameCommand(instance.Id.ToString(), svc, host), CancellationToken.None);
            r.IsSuccess.Should().BeTrue();
        }

        db.ChangeTracker.Clear();
        var stored = await db.Instances.AsNoTracking().SingleAsync(i => i.Id == instance.Id);
        stored.ServiceHostnames.Select(o => (o.ServiceName, o.Hostname)).Should().BeEquivalentTo(StagingHosts);
        outbox.Events.Should().HaveCount(3).And.AllBeOfType<InstanceServiceHostnameChangedIntegrationEvent>();
        var first = (InstanceServiceHostnameChangedIntegrationEvent)outbox.Events[0];
        first.InstanceId.Should().Be(instance.Id.ToString());
        first.ServiceName.Should().Be("api");
        first.Hostname.Should().Be("pruebas-api.inonetickets.lat");
        first.PreviousHostname.Should().BeNull();
    }

    [Fact]
    public async Task El_lookup_entrega_los_overrides_al_runner_y_sin_overrides_entrega_null()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var lookup = new EfInstanceLookup(db);

        (await lookup.GetByIdAsync(instance.Id.ToString(), CancellationToken.None))!
            .ServiceHostnames.Should().BeNull("sin override el runner debe ver exactamente lo de hoy");

        var handler = NewHandler(db, new RecordingOutbox());
        (await handler.Handle(new SetServiceHostnameCommand(instance.Id.ToString(), "API", "Pruebas-Api.InOneTickets.lat"),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();

        var view = await lookup.GetByIdAsync(instance.Id.ToString(), CancellationToken.None);
        view!.ServiceHostnames.Should().NotBeNull();
        // Nombre canónico del template y host normalizado; la clave es case-insensitive.
        view.ServiceHostnames!.Should().ContainKey("api");
        view.ServiceHostnames!["API"].Should().Be("pruebas-api.inonetickets.lat");
    }

    [Theory]
    [InlineData("api..inonetickets.lat", "hostname.format")]
    [InlineData("api.pruebas.inonetickets.lat/", "hostname.format")]
    [InlineData("pruebas-api", "hostname.format")]
    [InlineData("*.inonetickets.lat", "instance.service_hostname.wildcard")]
    public async Task Rechaza_un_hostname_invalido_sin_tocar_nada(string hostname, string expectedCode)
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var outbox = new RecordingOutbox();
        var handler = NewHandler(db, outbox);

        var r = await handler.Handle(
            new SetServiceHostnameCommand(instance.Id.ToString(), "api", hostname), CancellationToken.None);

        r.IsFailure.Should().BeTrue();
        r.Error.Type.Should().Be(ErrorType.Validation);
        r.Error.Code.Should().Be(expectedCode);
        outbox.Events.Should().BeEmpty();
        db.ChangeTracker.Clear();
        (await db.Instances.AsNoTracking().SingleAsync(i => i.Id == instance.Id)).ServiceHostnames.Should().BeEmpty();
    }

    [Fact]
    public async Task Rechaza_un_servicio_que_el_template_no_define()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var outbox = new RecordingOutbox();
        var handler = NewHandler(db, outbox);

        var r = await handler.Handle(
            new SetServiceHostnameCommand(instance.Id.ToString(), "apii", "pruebas-api.inonetickets.lat"),
            CancellationToken.None);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("instance.service_hostname.unknown_service");
        r.Error.Message.Should().Contain("api").And.Contain("org").And.Contain("admin");
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Cambiar_reporta_el_host_anterior_repetir_es_noop_y_quitar_vuelve_al_template()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var outbox = new RecordingOutbox();
        var handler = NewHandler(db, outbox);
        var id = instance.Id.ToString();

        (await handler.Handle(new SetServiceHostnameCommand(id, "org", "pruebas-org.inonetickets.lat"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        (await handler.Handle(new SetServiceHostnameCommand(id, "org", "pruebas-org.inonetickets.lat"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        outbox.Events.Should().HaveCount(1, "repetir el mismo host no reconcilia de nuevo");

        (await handler.Handle(new SetServiceHostnameCommand(id, "org", "staging-org.inonetickets.lat"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        var changed = outbox.Events[^1].Should().BeOfType<InstanceServiceHostnameChangedIntegrationEvent>().Subject;
        changed.PreviousHostname.Should().Be("pruebas-org.inonetickets.lat");
        changed.Hostname.Should().Be("staging-org.inonetickets.lat");

        (await handler.Handle(new SetServiceHostnameCommand(id, "org", null), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        var removed = outbox.Events[^1].Should().BeOfType<InstanceServiceHostnameChangedIntegrationEvent>().Subject;
        removed.Hostname.Should().BeNull();
        removed.PreviousHostname.Should().Be("staging-org.inonetickets.lat");

        db.ChangeTracker.Clear();
        (await db.Instances.AsNoTracking().SingleAsync(i => i.Id == instance.Id)).ServiceHostnames.Should().BeEmpty();
        (await new EfInstanceLookup(db).GetByIdAsync(id, CancellationToken.None))!.ServiceHostnames.Should().BeNull();

        (await handler.Handle(new SetServiceHostnameCommand(id, "org", null), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        outbox.Events.Should().HaveCount(3, "quitar lo que no existe es no-op");
    }

    [Fact]
    public async Task El_detalle_de_la_instancia_expone_los_overrides()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var handler = NewHandler(db, new RecordingOutbox());
        foreach (var (svc, host) in StagingHosts)
        {
            await handler.Handle(new SetServiceHostnameCommand(instance.Id.ToString(), svc, host), CancellationToken.None);
        }
        db.ChangeTracker.Clear();

        var detail = await new GetInstanceByIdHandler(db).Handle(
            new GetInstanceByIdQuery(instance.Id.ToString()), CancellationToken.None);

        detail.IsSuccess.Should().BeTrue();
        detail.Value.serviceHostnames!.Select(s => (s.serviceName, s.hostname))
            .Should().Equal(("admin", "pruebas-admin.inonetickets.lat"),
                ("api", "pruebas-api.inonetickets.lat"),
                ("org", "pruebas-org.inonetickets.lat"));
    }

    [Fact]
    public async Task Borrar_la_instancia_lleva_sus_hosts_de_servicio_al_evento_de_limpieza()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var handler = NewHandler(db, new RecordingOutbox());
        foreach (var (svc, host) in StagingHosts)
        {
            await handler.Handle(new SetServiceHostnameCommand(instance.Id.ToString(), svc, host), CancellationToken.None);
        }
        db.ChangeTracker.Clear();
        var outbox = new RecordingOutbox();

        var r = await new DeleteInstanceHandler(db, new FixedClock(Now), outbox).Handle(
            new DeleteInstanceCommand(instance.Id.ToString(), ForceEphemeral: true), CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        var removed = outbox.Events.Should().ContainSingle().Which.Should()
            .BeOfType<InstanceRemovedIntegrationEvent>().Subject;
        removed.ServiceHostnames.Should().BeEquivalentTo(StagingHosts.Select(h => h.Host));
    }

    // ---- G2 de OT-009 (ALTO): un override nunca puede quedarse con un host ajeno ----

    [Fact]
    public async Task Rechaza_el_host_de_una_ruta_de_produccion_typo_sin_pruebas()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        // Repro del G2: prod sirve api.inonetickets.lat (ruta con dueño = la instance de prod).
        var routes = new FakeRouteHosts(new RouteHostOwnerView("api.inonetickets.lat", "/", "ins_PROD"));
        var outbox = new RecordingOutbox();

        var r = await NewHandler(db, outbox, routes).Handle(
            new SetServiceHostnameCommand(instance.Id.ToString(), "api", "api.inonetickets.lat"), CancellationToken.None);

        r.IsFailure.Should().BeTrue();
        r.Error.Type.Should().Be(ErrorType.Conflict);
        r.Error.Code.Should().Be("instance.service_hostname.in_use");
        r.Error.Message.Should().Contain("ins_PROD");
        outbox.Events.Should().BeEmpty();
        db.ChangeTracker.Clear();
        (await db.Instances.AsNoTracking().SingleAsync(i => i.Id == instance.Id)).ServiceHostnames.Should().BeEmpty();
    }

    [Fact]
    public async Task Rechaza_el_host_de_una_ruta_sin_duenio()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var routes = new FakeRouteHosts(new RouteHostOwnerView("manual.inonetickets.lat", "/", null));

        var r = await NewHandler(db, new RecordingOutbox(), routes).Handle(
            new SetServiceHostnameCommand(instance.Id.ToString(), "api", "manual.inonetickets.lat"), CancellationToken.None);

        r.Error.Code.Should().Be("instance.service_hostname.in_use");
    }

    [Fact]
    public async Task Acepta_un_host_cuya_unica_ruta_es_de_esta_misma_instancia()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var routes = new FakeRouteHosts(
            new RouteHostOwnerView("pruebas-api.inonetickets.lat", "/", instance.Id.ToString()));

        var r = await NewHandler(db, new RecordingOutbox(), routes).Handle(
            new SetServiceHostnameCommand(instance.Id.ToString(), "api", "pruebas-api.inonetickets.lat"), CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("auto")]
    [InlineData("override")]
    public async Task Rechaza_un_host_que_otra_instancia_declara_como_suyo(string how)
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var tracked = await db.Instances.SingleAsync(i => i.Id == instance.Id);
        var other = Instance.Create(
            tracked.TemplateId, tracked.ClientId, "production", "vm_bogota", "inonetickets", "iot",
            null, null, null, autoDeployOnNewBuild: false, Now, slugOverride: "iot-prod");
        const string host = "otra.inonetickets.lat";
        switch (how)
        {
            case "custom": other.SetCustomDomain(host, Now); break;
            case "auto": other.SetAutoHostname(host, Now); break;
            default: other.SetServiceHostname("org", host, Now, out _); break;
        }
        db.Instances.Add(other);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var outbox = new RecordingOutbox();

        var r = await NewHandler(db, outbox).Handle(
            new SetServiceHostnameCommand(instance.Id.ToString(), "api", host), CancellationToken.None);

        r.Error.Code.Should().Be("instance.service_hostname.in_use");
        r.Error.Message.Should().Contain("iot-prod");
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Rechaza_el_host_raiz_propio_y_el_de_otro_servicio_de_la_misma_instancia()
    {
        await using var db = await fixture.CreateCleanDbContextAsync();
        var instance = Seed(db);
        var handler = NewHandler(db, new RecordingOutbox());
        var id = instance.Id.ToString();

        (await handler.Handle(new SetServiceHostnameCommand(id, "api", "pruebas.inonetickets.lat"), CancellationToken.None))
            .Error.Code.Should().Be("instance.service_hostname.in_use");

        (await handler.Handle(new SetServiceHostnameCommand(id, "org", "pruebas-org.inonetickets.lat"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        var dup = await handler.Handle(
            new SetServiceHostnameCommand(id, "admin", "pruebas-org.inonetickets.lat"), CancellationToken.None);
        dup.Error.Code.Should().Be("instance.service_hostname.in_use");
        dup.Error.Message.Should().Contain("'org'");
        // Re-fijar el MISMO servicio con su propio host no es un conflicto consigo mismo.
        (await handler.Handle(new SetServiceHostnameCommand(id, "org", "pruebas-org.inonetickets.lat"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
    }

    private static SetServiceHostnameHandler NewHandler(
        ProjectsDbContext db, RecordingOutbox outbox, FakeRouteHosts? routes = null)
        => new(db, new FixedClock(Now), outbox, routes ?? new FakeRouteHosts());

    private sealed class FakeRouteHosts(params RouteHostOwnerView[] routes) : IRouteHostLookup
    {
        public Task<IReadOnlyList<RouteHostOwnerView>> FindByHostnameAsync(string hostname, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<RouteHostOwnerView>>(
                [.. routes.Where(r => string.Equals(r.Hostname, hostname, StringComparison.OrdinalIgnoreCase))]);
    }

    private static readonly (string Service, string Host)[] StagingHosts =
    [
        ("api", "pruebas-api.inonetickets.lat"),
        ("org", "pruebas-org.inonetickets.lat"),
        ("admin", "pruebas-admin.inonetickets.lat"),
    ];

    private static Instance Seed(ProjectsDbContext db)
    {
        var project = Aethra.Modules.Projects.Domain.Project.Create(
            Slug.Create("inonetickets").Value, "InOneTickets", Now);
        var template = Template.Create(
            project.Id,
            Slug.Create("inonetickets").Value,
            "InOneTickets",
            TemplateSource.Create(GitRepoUrl.Create("https://github.com/acme/inonetickets").Value, "main"),
            TemplateBuild.Nixpacks(),
            "secret",
            new PlainWebhookSecretCodec(),
            Now);
        // Topología real de tpl_G44A20EM9DC7HCBXAPA6VYDFA8: publica sin hostname (= dominio de la
        // instancia) y api/org/admin con "{svc}.{domain}".
        template.ReplaceServices(
        [
            new TemplateService("publica", "", 3000, ["/"], [], "git"),
            new TemplateService("api", "", 5091, [], [], "git", Hostname: "api.{domain}"),
            new TemplateService("org", "", 3001, [], [], "git", Hostname: "org.{domain}"),
            new TemplateService("admin", "", 3002, [], [], "git", Hostname: "admin.{domain}"),
        ], Now);
        var client = Client.Create(project.Id, "iot", "InOneTickets", Now);
        var instance = Instance.Create(
            template.Id, client.Id, "staging", "vm_bogota", template.Slug.Value, client.Slug,
            null, null, null, autoDeployOnNewBuild: false, Now, slugOverride: "iot-stg");
        instance.SetCustomDomain("pruebas.inonetickets.lat", Now);

        db.Projects.Add(project);
        db.Templates.Add(template);
        db.Clients.Add(client);
        db.Instances.Add(instance);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return instance;
    }

    private sealed class RecordingOutbox : IOutboxWriter<ProjectsDbContext>
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public Task EnqueueAsync(IIntegrationEvent @event, CancellationToken ct)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class PlainWebhookSecretCodec : IWebhookSecretCodec
    {
        public byte[] Encode(string plainSecret) => System.Text.Encoding.UTF8.GetBytes(plainSecret);
        public string Decode(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);
    }
}
