using Aethra.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.Infrastructure.Persistence;

internal sealed class EfApiKeyRepository(IdentityDbContext db) : IApiKeyRepository
{
    public Task<ApiKey?> FindByHashAsync(byte[] hash, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hash);
        // AsNoTracking porque el handler de auth no muta nada; el MarkUsed se hace
        // en una fire-and-forget aparte (ver AethraApiKeyAuthHandler) con su propio scope.
        return db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash, ct);
    }

    public Task<ApiKey?> GetByIdAsync(ApiKeyId id, CancellationToken ct)
        => db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);

    public async Task<IReadOnlyList<ApiKey>> ListAllAsync(CancellationToken ct)
    {
        var items = await db.ApiKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return items;
    }
}
