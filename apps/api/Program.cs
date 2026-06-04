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
// consulte sus tablas. En producción se aplican explícitamente con `dotnet ef`
// pero la guarda IsDevelopment las habilita en localhost.
// -----------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyPendingMigrationsAsync();
}

// -----------------------------------------------------------------------------
// Pipeline HTTP
// -----------------------------------------------------------------------------
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// -----------------------------------------------------------------------------
// Endpoints
// -----------------------------------------------------------------------------
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapContextEndpoints();
app.MapNativeDeployEndpoints();
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
