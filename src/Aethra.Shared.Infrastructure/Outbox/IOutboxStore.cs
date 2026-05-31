namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// Lectura del outbox por el dispatcher. Implementacion concreta por modulo
/// (cada uno tiene su tabla outbox_messages en su propio schema).
/// </summary>
public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken ct);

    Task MarkProcessedAsync(Guid messageId, CancellationToken ct);

    Task MarkFailedAsync(Guid messageId, string error, DateTimeOffset nextAttemptAt, CancellationToken ct);
}
