using Aethra.Modules.Settings.Domain;
using Aethra.Shared.Contracts.Settings;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// Implementación EF de <see cref="IIntegrationCredentialResolver"/>. Descifra el valor en
/// memoria y dispara la actualización de <c>LastUsedAt</c> en un scope separado
/// fire-and-forget para no contaminar la transacción del consumidor.
/// </summary>
internal sealed class EfIntegrationCredentialResolver(
    SettingsDbContext db,
    IIntegrationCredentialCodec codec,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<EfIntegrationCredentialResolver> logger) : IIntegrationCredentialResolver
{
    public async Task<string?> GetSecretAsync(string credentialName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(credentialName);
        var normalized = credentialName.Trim().ToLowerInvariant();

        var row = await db.IntegrationCredentials
            .AsNoTracking()
            .Where(c => c.Name == normalized)
            .Select(c => new { c.Id, c.ValueCipher })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        // Descifrado: si la key de DataProtection se perdió la excepción burbujea — el caller
        // debe interpretarlo como "credencial inválida en runtime", no como "no existe".
        var plain = codec.Decode(row.ValueCipher);

        // Fire-and-forget: actualizar LastUsedAt en un scope nuevo. Si el consumidor está
        // dentro de una transacción y aborta, el touch sobrevive — es metadata cosmética.
        _ = TouchLastUsedAsync(row.Id, clock.UtcNow);
        return plain;
    }

    public async Task<bool> ExistsAsync(string credentialName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(credentialName);
        var normalized = credentialName.Trim().ToLowerInvariant();
        return await db.IntegrationCredentials
            .AsNoTracking()
            .AnyAsync(c => c.Name == normalized, ct)
            .ConfigureAwait(false);
    }

    private async Task TouchLastUsedAsync(IntegrationCredentialId id, DateTimeOffset now)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scoped = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
            var entity = await scoped.IntegrationCredentials.FirstOrDefaultAsync(c => c.Id == id).ConfigureAwait(false);
            if (entity is null)
            {
                return;
            }
            entity.MarkUsed(now);
            await scoped.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // No queremos romper al consumidor por un touch fallido.
            logger.LogWarning(ex, "No se pudo actualizar last_used_at de la credencial {CredentialId}", id);
        }
    }
}
