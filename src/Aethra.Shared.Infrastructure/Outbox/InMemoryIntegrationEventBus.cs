using Aethra.Shared.Kernel.Domain;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// Implementacion en proceso: simplemente publica via MediatR como INotification.
/// Los suscriptores cross-module implementan INotificationHandler&lt;TEvent&gt;.
///
/// <para><b>Importante</b>: <see cref="IMediator"/> se resuelve del <see cref="IServiceProvider"/>
/// que el caller pasa (típicamente el scope del dispatcher de outbox del módulo origen), no del
/// root provider. Sin esto, MediatR intentaría resolver <c>INotificationHandler&lt;T&gt;</c>
/// del root provider y fallaría porque los handlers cross-module requieren DbContexts scoped
/// (ej: <c>InstanceProvisionedHandler</c> del módulo Proxy depende de <c>ProxyDbContext</c>).</para>
///
/// Cuando un dia haya que separar modulos en procesos distintos, basta reemplazar esta
/// implementacion por una que escriba a Kafka/NATS/RabbitMQ sin tocar handlers.
/// </summary>
public sealed class InMemoryIntegrationEventBus(
    IServiceProvider serviceProvider,
    ILogger<InMemoryIntegrationEventBus> logger)
    : IIntegrationEventBus
{
    public async Task PublishAsync(IIntegrationEvent @event, CancellationToken ct)
    {
        if (@event is INotification notification)
        {
            // `IMediator` viene del scope del caller (no del root). Esto permite que
            // los handlers resuelvan dependencias scoped sin romper el contenedor.
            var mediator = serviceProvider.GetRequiredService<IMediator>();
            await mediator.Publish(notification, ct).ConfigureAwait(false);
            logger.LogDebug("Integration event publicado: {EventType} {EventId}",
                @event.GetType().Name, @event.EventId);
            return;
        }

        logger.LogWarning(
            "Integration event {EventType} no implementa INotification — sera ignorado por el bus.",
            @event.GetType().FullName);
    }
}
