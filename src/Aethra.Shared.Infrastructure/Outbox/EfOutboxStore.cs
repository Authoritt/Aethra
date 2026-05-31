using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Shared.Infrastructure.Outbox;

/// <summary>
/// Lectura del outbox por el dispatcher. Cada módulo tiene su propio
/// <see cref="AethraModuleDbContext"/> y, por ende, su propia tabla.
/// </summary>
public sealed class EfOutboxStore<TDbContext>(TDbContext dbContext) : IOutboxStore
    where TDbContext : AethraModuleDbContext
{
    public async Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.OccurredAt)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task MarkProcessedAsync(Guid messageId, CancellationToken ct)
    {
        await dbContext.OutboxMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ProcessedAt, DateTimeOffset.UtcNow)
                .SetProperty(m => m.Error, (string?)null), ct)
            .ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(Guid messageId, string error, DateTimeOffset nextAttemptAt, CancellationToken ct)
    {
        await dbContext.OutboxMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                .SetProperty(m => m.Error, error)
                .SetProperty(m => m.NextAttemptAt, nextAttemptAt), ct)
            .ConfigureAwait(false);
    }
}
