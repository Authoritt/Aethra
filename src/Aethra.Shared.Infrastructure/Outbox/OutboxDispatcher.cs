using System.Text.Json;
using Aethra.Shared.Kernel.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// BackgroundService que lee mensajes pendientes de un <see cref="IOutboxStore"/> y los publica
/// al <see cref="IIntegrationEventBus"/>. Garantia: at-least-once. Idempotencia es responsabilidad
/// de los handlers consumidores.
///
/// Backoff exponencial con jitter en caso de fallo del bus o deserializacion.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceProvider serviceProvider,
    IOutboxStore store,
    IIntegrationEventBus bus,
    ILogger<OutboxDispatcher> logger,
    OutboxDispatcherOptions options)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxDispatcher arrancando — batch={BatchSize} interval={IntervalMs}ms",
            options.BatchSize, options.PollIntervalMs);

        _ = serviceProvider; // gancho para resolver scopes por mensaje si hiciera falta

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await store.FetchPendingAsync(options.BatchSize, stoppingToken).ConfigureAwait(false);
                foreach (var msg in messages)
                {
                    await ProcessAsync(msg, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxDispatcher fallo en el loop principal");
            }

            await Task.Delay(options.PollIntervalMs, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessAsync(OutboxMessage msg, CancellationToken ct)
    {
        try
        {
            var type = Type.GetType(msg.Type)
                ?? throw new InvalidOperationException($"Tipo no resoluble: {msg.Type}");

            var deserialized = JsonSerializer.Deserialize(msg.Payload, type)
                ?? throw new InvalidOperationException($"No se pudo deserializar payload del tipo {type.Name}");

            if (deserialized is not IIntegrationEvent ev)
            {
                throw new InvalidOperationException(
                    $"Tipo {type.Name} no implementa IIntegrationEvent.");
            }

            await bus.PublishAsync(ev, ct).ConfigureAwait(false);
            await store.MarkProcessedAsync(msg.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var nextAttempt = DateTimeOffset.UtcNow + ComputeBackoff(msg.Attempts);
            await store.MarkFailedAsync(msg.Id, ex.Message, nextAttempt, ct).ConfigureAwait(false);
            logger.LogWarning(ex, "Outbox message {Id} ({Type}) fallo, reintento en {NextAttempt:o}",
                msg.Id, msg.Type, nextAttempt);
        }
    }

    private static TimeSpan ComputeBackoff(int attempts)
    {
        var baseSeconds = Math.Min(Math.Pow(2, attempts), 300);
        var jitter = Random.Shared.NextDouble() * 0.3 * baseSeconds;
        return TimeSpan.FromSeconds(baseSeconds + jitter);
    }
}

public sealed class OutboxDispatcherOptions
{
    public int BatchSize { get; set; } = 50;
    public int PollIntervalMs { get; set; } = 2000;
}
