using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Shared.Infrastructure.Modules;

/// <summary>
/// Helpers para que cada módulo registre su DbContext + outbox writer/store + dispatcher
/// con una sola llamada. F9.9: el <c>TransactionBehavior</c> NO se registra en este modelo
/// (no hay transacciones cross-DbContext en el monolith modular); cada writer cross-module
/// llama <c>SaveChangesAsync</c> internamente sobre su propio <c>DbContext</c>.
/// </summary>
public static class ModuleRegistrationExtensions
{
    public static IServiceCollection AddAethraModuleDbContext<TDbContext>(
        this IServiceCollection services,
        string connectionString,
        Action<DbContextOptionsBuilder>? configure = null)
        where TDbContext : AethraModuleDbContext
    {
        // Cada módulo guarda su propio __EFMigrationsHistory en su schema. Sin esto, EF usa
        // public.__EFMigrationsHistory compartido y dos migraciones con el mismo MigrationId
        // (timestamps generados en paralelo por agentes distintos) se confunden entre sí.
        var schemaName = ResolveSchemaName<TDbContext>();

        services.AddDbContext<TDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), errorCodesToAdd: null);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schemaName);
            });
            configure?.Invoke(options);
        });

        services.AddScoped<IOutboxWriter<TDbContext>, EfOutboxWriter<TDbContext>>();
        services.AddScoped<IOutboxStore<TDbContext>, EfOutboxStore<TDbContext>>();

        services.AddHostedService<ModuleOutboxDispatcherHost<TDbContext>>();
        // Purga periódica de mensajes ya procesados de la outbox de este módulo (la tabla crecía sin
        // tope: el dispatcher marca ProcessedAt pero nunca borra). Solo toca procesados antiguos.
        services.AddHostedService<ModuleOutboxPurgeHost<TDbContext>>();

        return services;
    }

    private static string ResolveSchemaName<TDbContext>() where TDbContext : AethraModuleDbContext
    {
        var optsBuilder = new DbContextOptionsBuilder<TDbContext>();
        optsBuilder.UseNpgsql("Host=ignored");
        var ctx = (TDbContext)Activator.CreateInstance(typeof(TDbContext), optsBuilder.Options)!;
        try { return ctx.SchemaName; }
        finally { ctx.Dispose(); }
    }
}

/// <summary>
/// Hosted service que crea un scope por módulo y ejecuta el dispatcher resolviendo
/// el <see cref="IOutboxStore"/> y <see cref="IIntegrationEventBus"/> de ese scope.
/// </summary>
internal sealed class ModuleOutboxDispatcherHost<TDbContext>(
    IServiceScopeFactory scopeFactory,
    Microsoft.Extensions.Options.IOptions<OutboxDispatcherOptions> options,
    Microsoft.Extensions.Logging.ILogger<ModuleOutboxDispatcherHost<TDbContext>> logger)
    : BackgroundService
    where TDbContext : AethraModuleDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var moduleName = typeof(TDbContext).Name;
        logger.LogInformation("Dispatcher de outbox para {Module} arrancando", moduleName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IOutboxStore<TDbContext>>();
                var bus = scope.ServiceProvider.GetRequiredService<IIntegrationEventBus>();
                var batch = await store.FetchPendingAsync(options.Value.BatchSize, stoppingToken).ConfigureAwait(false);

                foreach (var msg in batch)
                {
                    try
                    {
                        var type = Type.GetType(msg.Type)
                            ?? throw new InvalidOperationException($"Tipo no resoluble: {msg.Type}");
                        var deserialized = System.Text.Json.JsonSerializer.Deserialize(msg.Payload, type);
                        if (deserialized is Aethra.Shared.Kernel.Domain.IIntegrationEvent ev)
                        {
                            await bus.PublishAsync(ev, stoppingToken).ConfigureAwait(false);
                            await store.MarkProcessedAsync(msg.Id, stoppingToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        var next = DateTimeOffset.UtcNow + ComputeBackoff(msg.Attempts);
                        await store.MarkFailedAsync(msg.Id, ex.Message, next, stoppingToken).ConfigureAwait(false);
                        logger.LogWarning(ex, "Outbox {Module} msg {Id} falló, reintento {Next:o}", moduleName, msg.Id, next);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dispatcher {Module} falló en el loop principal", moduleName);
            }

            await Task.Delay(options.Value.PollIntervalMs, stoppingToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan ComputeBackoff(int attempts)
    {
        var baseSeconds = Math.Min(Math.Pow(2, attempts), 300);
        var jitter = Random.Shared.NextDouble() * 0.3 * baseSeconds;
        return TimeSpan.FromSeconds(baseSeconds + jitter);
    }
}

/// <summary>
/// Hosted service por módulo que purga los mensajes de outbox YA procesados
/// (<c>ProcessedAt &lt; now - RetentionDays</c>) de la tabla del módulo. El dispatcher marca
/// <c>ProcessedAt</c> pero nunca borra la fila → sin esto cada <c>outbox_messages</c> crece sin tope.
/// Mismo patrón que <see cref="ModuleOutboxDispatcherHost{TDbContext}"/> (un host genérico por
/// DbContext de módulo). Solo borra procesados antiguos vía <c>ExecuteDeleteAsync</c>: un mensaje
/// pendiente o fallido (reintentándose) nunca se toca. Best-effort: cualquier fallo se loguea y se
/// reintenta en el próximo ciclo sin tumbar el host.
/// </summary>
internal sealed class ModuleOutboxPurgeHost<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxDispatcherOptions> options,
    TimeProvider clock,
    ILogger<ModuleOutboxPurgeHost<TDbContext>> logger)
    : BackgroundService
    where TDbContext : AethraModuleDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = options.Value.OutboxRetentionDays;
        var moduleName = typeof(TDbContext).Name;
        if (retentionDays <= 0)
        {
            logger.LogInformation("OutboxPurge {Module} desactivado (OutboxRetentionDays <= 0).", moduleName);
            return;
        }
        var sweep = TimeSpan.FromHours(Math.Max(1, options.Value.OutboxSweepIntervalHours));

        // Delay inicial para no pegarle al boot (migraciones + arranque de dispatchers primero).
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(sweep);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
                var cutoff = clock.GetUtcNow() - TimeSpan.FromDays(retentionDays);
                var deleted = await db.OutboxMessages
                    .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken)
                    .ConfigureAwait(false);
                if (deleted > 0)
                {
                    logger.LogInformation(
                        "OutboxPurge {Module}: {Count} mensaje(s) procesado(s) borrado(s) (> {Days}d)",
                        moduleName, deleted, retentionDays);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OutboxPurge {Module} falló (se reintenta en el próximo ciclo)", moduleName);
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
