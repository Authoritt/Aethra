using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Shared.Infrastructure.Pipelines;

/// <summary>
/// Implementación EF Core de <see cref="IIdempotencyStore"/> contra <see cref="SharedDbContext"/>.
/// Limpieza de keys expiradas: TODO en F2+ — agregar BackgroundService que purgue cada hora.
/// </summary>
public sealed class EfIdempotencyStore(SharedDbContext dbContext) : IIdempotencyStore
{
    public async Task<string?> TryGetAsync(string key, string requestType, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = await dbContext.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.Key == key && k.RequestType == requestType && k.ExpiresAt > now, ct)
            .ConfigureAwait(false);
        return entry?.ResponseJson;
    }

    public async Task SaveAsync(string key, string requestType, string responseJson, TimeSpan ttl, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        dbContext.IdempotencyKeys.Add(new IdempotencyKey
        {
            Key = key,
            RequestType = requestType,
            ResponseJson = responseJson,
            CreatedAt = now,
            ExpiresAt = now + ttl,
        });
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
