using System.Reflection;
using Microsoft.AspNetCore.HttpOverrides;
using Aethra.Api.Bootstrap;
using Aethra.Api.Hubs;
using Aethra.Modules.Cloudflare;
using Aethra.Modules.Deployments;
using Aethra.Modules.Identity;
using Aethra.Modules.Mcp;
using Aethra.Modules.Metrics;
using Aethra.Modules.Monitoring;
using Aethra.Modules.Notes;
using Aethra.Modules.Notifications;
using Aethra.Modules.Projects;
using Aethra.Modules.Proxy;
using Aethra.Modules.Proxy.Presentation;
using Aethra.Modules.Services;
using Aethra.Modules.Settings;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Vms;
using Aethra.Modules.Vms.Authentication;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Infrastructure.Persistence;
using Aethra.Shared.Infrastructure.Pipelines;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Logging — Serilog leído desde configuración.
// -----------------------------------------------------------------------------
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services));

// -----------------------------------------------------------------------------
// Primitivos transversales
// -----------------------------------------------------------------------------
builder.Services.AddSingleton<IClock, Aethra.Shared.Kernel.Time.SystemClock>();
builder.Services.AddHttpContextAccessor();

// DataProtection persistido — protege secretos cifrados (TLS PFX, ACME account key,
// idempotency cache, etc.). En Development apunta a un directorio del usuario; en producción
// debe ir a un volumen persistente o key vault. Si las keys se pierden los secretos cifrados
// quedan ilegibles.
var dpKeyDir = builder.Configuration["DataProtection:KeyDir"]
    ?? Path.Combine(builder.Environment.ContentRootPath, ".aspnet-keys");
Directory.CreateDirectory(dpKeyDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeyDir))
    .SetApplicationName("aethra");

// -----------------------------------------------------------------------------
// MediatR — escanea todos los ensamblados de módulos para handlers/validators/eventos.
// El orden de los behaviors importa: Logging → Idempotency → Validation → Transaction → Handler.
// -----------------------------------------------------------------------------
var moduleAssemblies = new[]
{
    typeof(ProjectsModule).Assembly,
    typeof(DeploymentsModule).Assembly,
    typeof(ServicesModule).Assembly,
    typeof(ProxyModule).Assembly,
    typeof(VmsModule).Assembly,
    typeof(MetricsModule).Assembly,
    typeof(MonitoringModule).Assembly,
    typeof(CloudflareModule).Assembly,
    typeof(NotesModule).Assembly,
    typeof(NotificationsModule).Assembly,
    typeof(IdentityModule).Assembly,
    typeof(McpModule).Assembly,
    typeof(SettingsModule).Assembly,
};

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(moduleAssemblies);
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(IdempotencyBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    // TransactionBehavior se registra por modulo (necesita su propio DbContext).
});

builder.Services.AddValidatorsFromAssemblies(moduleAssemblies);

// -----------------------------------------------------------------------------
// Outbox: bus in-memory. El dispatcher por módulo se registra dentro de cada AddXModule.
// -----------------------------------------------------------------------------
// Scoped: resuelve IMediator del scope del dispatcher del módulo, no del root.
// Sin esto, MediatR no puede resolver handlers cross-module que dependen de DbContexts scoped.
builder.Services.AddScoped<IIntegrationEventBus, InMemoryIntegrationEventBus>();
// F13 — orquestador del deploy nativo multi-contenedor (endpoint manual + auto-trigger webhook).
builder.Services.AddScoped<Aethra.Api.Bootstrap.NativeDeployRunner>();
// F13.11 — despliega el connector cloudflared como contenedor gestionado (flip a túnel remoto desde UI).
builder.Services.AddScoped<Aethra.Api.Bootstrap.CloudflareConnectorDeployer>();
builder.Services.Configure<OutboxDispatcherOptions>(builder.Configuration.GetSection("Outbox"));

// -----------------------------------------------------------------------------
// SharedDbContext (idempotency_keys) + IdempotencyStore que usa IdempotencyBehavior.
// -----------------------------------------------------------------------------
var aethraConnection = builder.Configuration.GetConnectionString("Aethra")
    ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");
builder.Services.AddDbContext<SharedDbContext>(o => o.UseNpgsql(aethraConnection));
builder.Services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
// F9.10 D1: purga periódica de keys expiradas (antes la tabla crecía sin tope).
// TryAddSingleton — el módulo Proxy también lo registra; gana el primero.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddHostedService<IdempotencyPurgeWorker>();

// -----------------------------------------------------------------------------
// SignalR central→satélite (F9.8C): RPC real con correlation tracking.
//   - SatelliteConnectionRegistry: in-memory map vmId → connectionId (registrado
//     por SatelliteHub.OnConnected/Disconnected).
//   - SignalRSatelliteRpcClient: implementa ISatelliteRpcClient (lo usan los
//     orquestadores) e ISatelliteRpcCallbacks (lo invoca SatelliteHub al recibir
//     respuestas del satélite). Singleton para compartir el dict de pendientes.
// -----------------------------------------------------------------------------
builder.Services.AddSingleton<Aethra.Shared.Contracts.Containers.ISatelliteConnectionRegistry,
    SatelliteConnectionRegistry>();
builder.Services.AddSingleton<SignalRSatelliteRpcClient>();
builder.Services.AddSingleton<Aethra.Shared.Contracts.Containers.ISatelliteRpcClient>(
    sp => sp.GetRequiredService<SignalRSatelliteRpcClient>());
builder.Services.AddSingleton<Aethra.Shared.Contracts.Containers.ISatelliteRpcCallbacks>(
    sp => sp.GetRequiredService<SignalRSatelliteRpcClient>());

// Capacidad de satélites (disco libre) + backend de backups satellite:// — viven en el host porque
// dependen de ISatelliteRpcClient/Vms. SatelliteBackupStorage se suma al IEnumerable<IBackupStorage>
// que inyecta el BackupOrchestrator (junto a volume:// y s3://).
builder.Services.AddScoped<Aethra.Shared.Contracts.Containers.ISatelliteCapacityProvider,
    Aethra.Api.Hubs.MediatrSatelliteCapacityProvider>();
builder.Services.AddScoped<Aethra.Modules.Services.Infrastructure.Backup.IBackupStorage,
    Aethra.Modules.Services.Infrastructure.Backup.SatelliteBackupStorage>();

// F11.4 — Notifier que reenvía progreso de install al frontend via DashboardHub.
builder.Services.AddSingleton<Aethra.Shared.Contracts.Vms.IInstallProgressNotifier,
    InstallProgressNotifier>();

// -----------------------------------------------------------------------------
// Módulos — cada uno se hace cargo de su DbContext, handlers específicos y endpoints.
// -----------------------------------------------------------------------------
builder.Services
    .AddIdentityModule(builder.Configuration)
    .AddProjectsModule(builder.Configuration)
    .AddDeploymentsModule(builder.Configuration)
    .AddServicesModule(builder.Configuration)
    .AddProxyModule(builder.Configuration)
    .AddVmsModule(builder.Configuration)
    .AddMetricsModule(builder.Configuration)
    .AddMonitoringModule(builder.Configuration)
    .AddCloudflareModule(builder.Configuration)
    .AddNotesModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration)
    .AddMcpModule(builder.Configuration)
    .AddSettingsModule(builder.Configuration);

// -----------------------------------------------------------------------------
// Auth: cookie single-user para UI + token X-Satellite-Token para SignalR de satélite.
// -----------------------------------------------------------------------------
builder.Services
    .AddAuthentication(AuthSchemes.Cookie)
    .AddCookie(AuthSchemes.Cookie, options =>
    {
        options.Cookie.Name = "aethra.sid";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/auth/login";
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    })
    .AddScheme<SatelliteTokenAuthOptions, SatelliteTokenAuthHandler>(
        SatelliteAuthSchemes.TokenHeader, _ => { })
    .AddAethraApiKeyAuth(AuthSchemes.ApiKey);

// Policy default acepta Cookie o ApiKey — los endpoints REST de los módulos pueden
// llamarse por humanos (cookie) o agentes (api key). Endpoints sensibles (gestión de
// api-keys mismas) sobreescriben con RequireAuthorization(scheme: Cookie) explícito.
builder.Services.AddAuthorization(opts =>
{
    opts.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
            AuthSchemes.Cookie, AuthSchemes.ApiKey)
        .RequireAuthenticatedUser()
        .Build();
    // Policy explícita para endpoints sensibles que NO deben aceptar API keys
    // (gestión de api-keys, integraciones, etc.). Forzar AddAuthenticationSchemes en
    // un AuthorizationPolicyBuilder hace que el middleware solo procese ese scheme
    // — un RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ... })
    // sobre un endpoint NO filtra realmente porque la default policy ya autenticó al
    // usuario con cualquier scheme habilitado.
    opts.AddPolicy("CookieOnly", p => p
        .AddAuthenticationSchemes(AuthSchemes.Cookie)
        .RequireAuthenticatedUser());
    opts.AddApiKeyScopePolicies();
    // OT-012 (H2): /openapi solo para admin, sea cual sea el entorno.
    opts.AddOpenApiAdminPolicy();
});

// OT-012: limitador propio de /auth/login y /auth/login/totp (por IP real tras el tunel).
builder.Services.AddAethraLoginRateLimiter(builder.Configuration);

// -----------------------------------------------------------------------------
// SignalR: hub de satélite (entrada de métricas) + hub de dashboard (push al frontend).
// -----------------------------------------------------------------------------
builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
    // El satélite responde BuildImage con BuildResult.LogLines = el log de build COMPLETO. Para
    // builds reales (imágenes .NET/Next) eso supera de lejos el default de 32KB; SignalR descartaba
    // el mensaje en el central → el ack del build nunca llegaba y deploy-native moría por timeout
    // (900s). Subimos el límite de recepción para que las respuestas de build grandes lleguen.
    o.MaximumReceiveMessageSize = 64 * 1024 * 1024; // 64 MiB
});

// DashboardForwarder vive en apps/api (no en un módulo). Lo registramos escaneando
// el ensamblado del API para que MediatR encuentre el INotificationHandler.
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

// -----------------------------------------------------------------------------
// YARP reverse proxy. La configuración (rutas, clusters) viene de la BD vía
// DatabaseProxyConfigProvider registrado por ProxyModule.AddProxyModule().
// -----------------------------------------------------------------------------
builder.Services.AddReverseProxy();

// -----------------------------------------------------------------------------
// HTTP API + OpenAPI 3.1 nativo de .NET 10.
// -----------------------------------------------------------------------------
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:3000", "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

// -----------------------------------------------------------------------------
// Migraciones EF — deben correr antes de que cualquier provider singleton (YARP)
// consulte sus tablas.
//
// En Development se aplican solas. En Production NO, porque en un despliegue
// gestionado las aplica el pipeline con `dotnet ef` y no queremos que dos
// instancias migren a la vez.
//
// Pero una instalación NUEVA autohospedada (docker compose) corre en Production
// y no tiene pipeline: sin esto la BD queda vacía PARA SIEMPRE, la API arranca,
// responde, y cada dispatcher de outbox gira fallando contra tablas que no
// existen. Se ve viva y no lo está. Por eso existe la bandera explícita:
// quien se autohospeda pone Aethra__ApplyMigrationsOnStart=true (el compose ya
// lo hace) y quien tiene pipeline no cambia nada.
// -----------------------------------------------------------------------------
var applyMigrations = app.Environment.IsDevelopment()
    || app.Configuration.GetValue("Aethra:ApplyMigrationsOnStart", false);
if (applyMigrations)
{
    await app.Services.ApplyPendingMigrationsAsync();
}

// Postura de autenticacion, declarada AL ARRANCAR y no cuando alguien entra.
//
// El log de /auth/login avisa cuando la rama de bootstrap SE TOMA. Eso deja un hueco:
// un contador a cero no distingue "nunca hizo falta" de "nunca alcanzable", asi que el
// silencio no informa. Esto lo cierra por el otro lado -- dice si la rama esta ABIERTA,
// sin esperar a que alguien la use.
//
// Best-effort: si la BD no responde todavia (pipeline que migra aparte), no se tumba el
// arranque por un mensaje de diagnostico.
try
{
    using var arranque = app.Services.CreateScope();
    var usuarios = await arranque.ServiceProvider
        .GetRequiredService<Aethra.Modules.Identity.Infrastructure.EfUserStore>()
        .CountAsync(CancellationToken.None);
    var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aethra.Auth.Postura");
    if (Aethra.Modules.Identity.Domain.BootstrapLogin.Evaluar(usuarios)
        == Aethra.Modules.Identity.Domain.BootstrapLoginPosture.Abierto)
    {
        log.LogWarning(
            Aethra.Modules.Identity.Domain.BootstrapLogin.PlantillaAbierto,
            usuarios,
            applyMigrations);
    }
    else
    {
        log.LogInformation(
            Aethra.Modules.Identity.Domain.BootstrapLogin.PlantillaCerrado,
            usuarios);
    }
}
catch (Exception ex)
{
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aethra.Auth.Postura")
        .LogWarning(ex, "ARRANQUE: no se pudo determinar la postura de autenticacion ({Tipo}).",
            ex.GetType().Name);
}

// -----------------------------------------------------------------------------
// Pipeline HTTP
// -----------------------------------------------------------------------------

// PRIMERO DE TODO, y por un motivo que costo un incidente: Aethra vive detras del tunel de
// Cloudflare, que TERMINA EL TLS y nos habla por http a 127.0.0.1:5080. Sin esto, el
// `Request.Scheme` de CADA peticion que entra aqui es "http", aunque el usuario este en https.
//
// Eso no se queda en Aethra: este proceso ademas hace de REVERSE PROXY (MapReverseProxy). YARP
// construye el `X-Forwarded-Proto` que manda a las apps de abajo a partir de NUESTRO esquema, asi
// que un esquema mal resuelto aqui se propaga —o se omite— hacia todas ellas.
//
// Consecuencia real (2026-08-12): el login de Ekippo llevaba 4 dias devolviendo 500 en produccion.
// Su antiforgery tiene `Cookie.SecurePolicy = Always` y lanzaba con "the current request is not an
// SSL request". Medido contra el contenedor: sin `X-Forwarded-Proto` -> 500; con
// `X-Forwarded-Proto: https` -> 200. Y era invisible: con credenciales malas respondia 401 normal,
// asi que solo fallaba el camino de EXITO y el monitor —que mira /login— seguia en verde.
//
// Afecta a cualquier app detras del proxy que dependa del esquema: cookies Secure, redirecciones a
// https y generacion de URLs absolutas (enlaces de correo, callbacks OAuth).
//
// KnownProxies/KnownIPNetworks se limpian a proposito: el salto anterior es el tunel, que no esta
// en loopback ni en un rango fijo. Es seguro porque el unico camino publico hasta este puerto es el
// propio tunel — el puerto 5080 escucha SOLO en 127.0.0.1 (no esta publicado en la interfaz de red),
// asi que la cabecera no es falsificable desde fuera.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost,
};
forwardedHeadersOptions.KnownProxies.Clear();
forwardedHeadersOptions.KnownIPNetworks.Clear();
// OT-012: el par TCP real se guarda ANTES de que UseForwardedHeaders reescriba RemoteIpAddress
// con X-Forwarded-For (falsificable: KnownProxies esta vacio). Lo usa el limitador del login.
app.UseRawPeerCapture();
// G2 de OT-012: Host localhost/127.0.0.1 en rutas del panel solo desde un par local real.
var panelPeers = PanelPeers.FromConfiguration(app.Configuration);
if (PanelSurface.ResolvePanelHosts(app.Configuration) is not null)
{
    app.UsePanelLoopbackHostGuard(panelPeers);
    if (!panelPeers.HasConfiguredLocalPeers)
    {
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aethra.Panel.Surface").LogWarning(
            "ARRANQUE: Panel:LocalPeers vacio -> solo 127.0.0.0/8 y ::1 son locales. Si el central es un "
            + "contenedor con el puerto publicado, fija Panel__LocalPeers__0=<gateway del bridge>: si no, "
            + "los scripts con Host: localhost reciben 404 y CF-Connecting-IP no se acepta.");
    }
}
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();


app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
// Solo actua sobre endpoints con RequireRateLimiting (el login): sin limitador global, el proxy
// de las apps no lo nota. El endpoint ya esta resuelto: el UseRouting implicito va primero.
app.UseRateLimiter();

// -----------------------------------------------------------------------------
// Endpoints
// -----------------------------------------------------------------------------
// OT-012 (H1): toda la superficie propia de Aethra va en el grupo del panel (RequireHost con
// Panel:Hosts + loopback). Por cualquier otro host esas rutas no casan y la peticion sigue al
// catch-all de YARP de ese host: las apps reciben sus propias /auth, /api, /hubs como si Aethra
// no existiera. Ver PanelSurface.
var panelHosts = PanelSurface.ResolvePanelHosts(app.Configuration);
var surfaceLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aethra.Panel.Surface");
if (panelHosts is null)
{
    surfaceLog.LogWarning(
        "ARRANQUE: Panel:Hosts vacio -> las rutas de Aethra responden en CUALQUIER host que llegue "
        + "a este proceso (incluidos los de las apps del proxy). En produccion fija "
        + "Panel__Hosts__0=<host del panel>.");
}
else
{
    surfaceLog.LogInformation("ARRANQUE: rutas de Aethra restringidas a los hosts {Hosts}",
        string.Join(", ", panelHosts));
}

// Fuera del grupo, a proposito (atienden en TODOS los hosts, como antes):
//  - /health y /health/ready: hoy los hosts de las apps los devuelven desde Aethra y puede haber
//    monitores apuntando ahi; moverlos cambia lo que ve una app/monitor. No exponen nada.
//  - /.well-known/acme-challenge: el HTTP-01 de Let's Encrypt llega por el host de CADA app.
app.MapHealthEndpoints();
app.MapAcmeChallengeEndpoint();

var panel = app.MapPanelGroup(panelHosts);

// El documento OpenAPI es el unico artefacto DERIVADO del registro de rutas: no se mantiene
// a mano, asi que no puede desviarse del codigo como si puede un mapa de arquitectura o un
// README. OT-012 (H2): exige sesion o clave de admin SIEMPRE — produccion corre en Development
// a proposito y antes eso lo dejaba anonimo.
panel.MapAethraOpenApi();

panel.MapAuthEndpoints();
panel.MapContextEndpoints();
panel.MapNativeDeployEndpoints();
panel.MapOperationsEndpoints();
panel.MapProjectsModuleEndpoints();
panel.MapVmsModuleEndpoints();
panel.MapMetricsModuleEndpoints();
panel.MapProxyModuleEndpoints();
panel.MapDeploymentsModuleEndpoints();
panel.MapServicesModuleEndpoints();
panel.MapCloudflareModuleEndpoints();
panel.MapMonitoringModuleEndpoints();
panel.MapNotesModuleEndpoints();
panel.MapNotificationsModuleEndpoints();
panel.MapIdentityModuleEndpoints();
panel.MapMcpModuleEndpoints();
panel.MapSettingsModuleEndpoints();
panel.MapDashboardHub();

// YARP reverse proxy: catch-all al final. Cualquier hostname no manejado por endpoints
// REST/hubs anteriores se enruta vía YARP usando la config en la BD.
app.MapReverseProxy();

app.Run();

// Necesario para WebApplicationFactory en tests de integración.
namespace Aethra.Api
{
    public partial class Program;
}
