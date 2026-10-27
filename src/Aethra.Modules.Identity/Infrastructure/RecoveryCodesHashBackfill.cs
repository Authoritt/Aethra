using System.Security.Cryptography;
using Aethra.Modules.Identity.Domain.Totp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Identity.Infrastructure;

/// <summary>
/// GH #91: Migración idempotente y transaccional para convertir códigos de recuperación TOTP
/// legacy (almacenados como ciphertext reversible con DataProtection) a hashes one-way (Argon2id).
/// </summary>
public static class RecoveryCodesHashBackfill
{
    // ASCII "AETHRA", seguido de 0x0091, como clave estable de PostgreSQL advisory lock.
    private const long AdvisoryLockKey = 0x4145544852410091;

    public static async Task<int> BackfillAsync(
        IdentityDbContext db,
        ITotpSecretCodec codec,
        ILogger logger,
        CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database
                .BeginTransactionAsync(ct)
                .ConfigureAwait(false);

            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})",
                        ct)
                    .ConfigureAwait(false);
            }

            db.ChangeTracker.Clear();
            var users = await db.Users
                .Where(u => u.TotpEnabled && u.TotpRecoveryCodesCipher != null)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var migratedCount = 0;
            foreach (var user in users)
            {
                if (user.TotpRecoveryCodesCipher is null || RecoveryCodes.IsHashed(user.TotpRecoveryCodesCipher))
                {
                    continue;
                }

                try
                {
                    // Si el payload no se puede descifrar (anillo de claves ausente o rotado al arrancar),
                    // NO se trata el ciphertext como texto plano: hashear esa basura borraría para siempre
                    // los códigos reales del usuario. Se deja en legacy; la migración perezosa del login
                    // lo convertirá cuando el usuario use un código con las claves disponibles.
                    byte[] unpackedBytes;
                    try
                    {
                        unpackedBytes = codec.Unprotect(user.TotpRecoveryCodesCipher);
                    }
                    catch (CryptographicException ex)
                    {
                        logger.LogWarning(ex,
                            "Recovery codes del usuario {UserId} no descifrables; se dejan en formato legacy", user.Id);
                        continue;
                    }

                    var codes = RecoveryCodes.Unpack(unpackedBytes);
                    if (codes.Count == 0 || !codes.All(RecoveryCodes.LooksLikeRecoveryCode))
                    {
                        logger.LogWarning(
                            "Recovery codes del usuario {UserId} con formato inesperado; se dejan en formato legacy", user.Id);
                        continue;
                    }

                    var hashed = RecoveryCodes.Hash(codes);
                    db.Entry(user).Property(u => u.TotpRecoveryCodesCipher).CurrentValue = hashed;
                    migratedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "No se pudo migrar recovery codes para usuario {UserId}", user.Id);
                }
            }

            if (migratedCount > 0)
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                logger.LogInformation("Migrados {Count} conjuntos de recovery codes legacy a hashes one-way.", migratedCount);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return migratedCount;
        }).ConfigureAwait(false);
    }
}
