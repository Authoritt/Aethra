using Aethra.Shared.Infrastructure.Persistence;

namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// Lectura del outbox por el dispatcher. Implementacion concreta por modulo
/// (cada uno tiene su tabla outbox_messages en su propio schema).
///
/// Genérico por <typeparamref name="TDbContext"/> para que cada módulo resuelva su propio
/// store sin colisión DI (ver <see cref="IOutboxWriter{TDbContext}"/> para el mismo patrón).
/// </summary>
public interface IOutboxStore<TDbContext> where TDbContext : AethraModuleDbContext
{
    Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken ct);

    Task MarkProcessedAsync(Guid messageId, CancellationToken ct);

    Task MarkFailedAsync(Guid messageId, string error, DateTimeOffset nextAttemptAt, CancellationToken ct);
}
