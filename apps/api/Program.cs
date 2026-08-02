using System.Reflection;
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
});

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

// -----------------------------------------------------------------------------
// Pipeline HTTP
// -----------------------------------------------------------------------------
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();


app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// -----------------------------------------------------------------------------
// Endpoints
// -----------------------------------------------------------------------------
// El documento OpenAPI es el unico artefacto DERIVADO del registro de rutas: no se mantiene
// a mano, asi que no puede desviarse del codigo como si puede un mapa de arquitectura o un
// README. Estaba mapeado solo en Development, es decir, ausente exactamente del despliegue
// donde alguien necesita preguntar que rutas existen de verdad.
//
// Fuera de Development exige autenticacion. El codigo es abierto, asi que la superficie no
// es un secreto; pero servirsela a un escaner anonimo no aporta nada a nadie.
var openApi = app.MapOpenApi();
if (!app.Environment.IsDevelopment())
{
    openApi.RequireAuthorization();
}

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapContextEndpoints();
app.MapNativeDeployEndpoints();
app.MapOperationsEndpoints();
app.MapProjectsModuleEndpoints();
app.MapVmsModuleEndpoints();
app.MapMetricsModuleEndpoints();
app.MapProxyModuleEndpoints();
app.MapDeploymentsModuleEndpoints();
app.MapServicesModuleEndpoints();
app.MapCloudflareModuleEndpoints();
app.MapMonitoringModuleEndpoints();
app.MapNotesModuleEndpoints();
app.MapNotificationsModuleEndpoints();
app.MapIdentityModuleEndpoints();
app.MapMcpModuleEndpoints();
app.MapSettingsModuleEndpoints();
app.MapDashboardHub();

// YARP reverse proxy: catch-all al final. Cualquier hostname no manejado por endpoints
// REST/hubs anteriores se enruta vía YARP usando la config en la BD.
app.MapReverseProxy();

app.Run();

// Necesario para WebApplicationFactory en tests de integración.
namespace Aethra.Api
{
    public partial class Program;
}
