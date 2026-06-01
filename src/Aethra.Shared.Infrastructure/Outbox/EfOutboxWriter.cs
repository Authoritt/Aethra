using System.Text.Json;
using Aethra.Shared.Infrastructure.Persistence;
using Aethra.Shared.Kernel.Domain;

namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// Escribe integration events al outbox del módulo emisor. Se llama desde handlers que hacen
/// <c>SaveChangesAsync</c> sobre su propio DbContext al final del Handle: el registro del
/// outbox se persiste en la misma llamada a <c>SaveChangesAsync</c> que el cambio de estado,
/// lo que garantiza que el evento se publica si y solo si el cambio persiste.
/// </summary>
public sealed class EfOutboxWriter<TDbContext>(TDbContext dbContext) : IOutboxWriter<TDbContext>
    where TDbContext : AethraModuleDbContext
{
    public Task EnqueueAsync(IIntegrationEvent @event, CancellationToken ct)
    {
        var msg = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = @event.GetType().AssemblyQualifiedName
                ?? throw new InvalidOperationException("AssemblyQualifiedName nulo en evento."),
            Payload = JsonSerializer.Serialize((object)@event),
            OccurredAt = @event.OccurredAt,
            Attempts = 0,
        };
        dbContext.OutboxMessages.Add(msg);
        return Task.CompletedTask;
    }
}
