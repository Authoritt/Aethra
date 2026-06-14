using Aethra.Modules.Services.Infrastructure;
using Aethra.Modules.Services.Infrastructure.Backup;
using Aethra.Modules.Services.Infrastructure.Binding;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Modules.Services.Infrastructure.Scheduling;
using Aethra.Modules.Services.Presentation;
using Aethra.Modules.Services.Templates;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aethra.Modules.Services;

/// <summary>
/// Punto de entrada del módulo Services.
///
/// Llamadas desde apps/api/Program.cs:
/// - <see cref="AddServicesModule"/> en builder.Services para DI.
/// - <see cref="MapServicesModuleEndpoints"/> en app después de UseAuthorization() para rutas REST.
/// </summary>
public static class ServicesModule
{
    public static IServiceCollection AddServicesModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<ServicesDbContext>(conn);

        // Catálogo de plantillas one-click (postgres-16, redis-7, rabbitmq-3-mgmt) cargado
        // desde recursos embebidos al construir el singleton.
        services.AddSingleton<IServiceTemplateCatalog, EmbeddedServiceTemplateCatalog>();

        services.AddServicesProvisioners();

        // Codec de credenciales del binding y mapper de env vars (DATABASE_URL, etc.).
        services.AddSingleton<IBindingCredentialsCodec, DataProtectionBindingCredentialsCodec>();
        services.AddSingleton<IBindingEnvVarMapper, DefaultBindingEnvVarMapper>();

        // F11.3B: backups. Engines por tipo + storages por scheme + orchestrator scoped.
        services.AddScoped<IBackupEngine, PostgresBackupEngine>();
        services.AddScoped<IBackupEngine, RedisBackupEngine>();
        services.AddScoped<IBackupEngine, RabbitMqBackupEngine>();
        services.AddScoped<IBackupStorage, VolumeBackupStorage>();
        services.AddScoped<IBackupStorage, S3BackupStorage>();
        services.AddScoped<BackupOrchestrator>();

        // HttpClient para llamadas HTTP del rabbit engine y S3 storage.
        services.AddHttpClient("services-backup", c => c.Timeout = TimeSpan.FromMinutes(5));

        services.AddHostedService<BackupWorker>();

        // F12.1A: scheduled jobs por servicio (cron + docker exec via satellite RPC).
        services.AddSingleton<ScheduledJobWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ScheduledJobWorker>());

        // Retención de corridas (stdout/stderr por run) — evita crecimiento ilimitado. Sección "ScheduledJobs".
        services.Configure<ScheduledJobRunRetentionOptions>(configuration.GetSection("ScheduledJobs"));
        services.AddHostedService<ScheduledJobRunRetentionWorker>();

        return services;
    }

    public static IEndpointRouteBuilder MapServicesModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapServicesEndpoints();
}
