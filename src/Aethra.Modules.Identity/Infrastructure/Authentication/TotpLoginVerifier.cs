using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Domain.Totp;
using Aethra.Modules.Identity.UseCases.Totp;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// F12.1B — verifier publico que el flujo de login (AuthEndpoints) usa para validar el
/// segundo paso del login (TOTP o recovery code). El handler MediatR equivalente no se usa
/// para esto porque login no es un comando MediatR sino un minimal endpoint directo.
/// </summary>
public interface ITotpLoginVerifier
{
    /// <summary>
    /// Devuelve true si <paramref name="code"/> es un TOTP/recovery valido para <paramref name="user"/>.
    /// Si es un recovery code, lo marca usado y persiste el cambio.
    /// </summary>
    Task<bool> VerifyAsync(User user, string code, CancellationToken ct);
}

internal sealed class TotpLoginVerifier(
    IdentityDbContext db,
    ITotpSecretCodec codec,
    IClock clock,
    TotpChallengeStore challenges) : ITotpLoginVerifier
{
    public async Task<bool> VerifyAsync(User user, string code, CancellationToken ct)
    {
        var isRecoveryCode = !string.IsNullOrWhiteSpace(code)
            && RecoveryCodes.LooksLikeRecoveryCode(code.Trim());

        if (!isRecoveryCode)
        {
            return await VerifyAndConsumeAsync(user, code, transaction: null, ct).ConfigureAwait(false);
        }

        // Cada reintento debe partir de la misma foto en memoria. VerifyRecoveryAsync sincroniza
        // la mascara rastreada tras su UPDATE condicional y el store limpia el challenge al usarlo;
        // si luego falla el commit, la transaccion revierte la BD pero esos dos estados quedan en
        // memoria. Restaurarlos evita que el siguiente intento rechace un codigo aun disponible.
        var entry = db.Entry(user);
        var usedMask = entry.Property(u => u.TotpRecoveryCodesUsedMask);
        var updatedAt = entry.Property(u => u.UpdatedAt);
        var initialUsedMask = (usedMask.CurrentValue, usedMask.OriginalValue, usedMask.IsModified);
        var initialUpdatedAt = (updatedAt.CurrentValue, updatedAt.OriginalValue, updatedAt.IsModified);
        var initialChallengeId = challenges.CurrentChallengeId;
        var initialFailedAttempts = initialChallengeId is null
            ? null
            : await db.TotpChallenges
                .AsNoTracking()
                .Where(challenge => challenge.Id == initialChallengeId)
                .Select(challenge => (int?)challenge.FailedAttempts)
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);

        var outcome = false;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<object?, bool>(
            state: null,
            operation: async (_, _, operationCt) =>
            {
                outcome = false;
                RestoreTracked(usedMask, initialUsedMask);
                RestoreTracked(updatedAt, initialUpdatedAt);
                challenges.RestoreCurrent(initialChallengeId);

                await using var transaction = await db.Database.BeginTransactionAsync(operationCt)
                    .ConfigureAwait(false);
                outcome = await VerifyAndConsumeAsync(user, code, transaction, operationCt)
                    .ConfigureAwait(false);
                return outcome;
            },
            verifySucceeded: async (_, _, verificationCt) =>
            {
                // Si CommitAsync falla despues de que PostgreSQL confirme, VerifyAndConsumeAsync no
                // alcanza a devolver y `outcome` sigue en false. Un codigo valido deja el nuevo bit
                // en la mascara rastreada; uno invalido deja en BD el siguiente contador de fallos.
                // Cualquiera de los dos confirma esta unidad sin repetir sus efectos.
                var consumedRecoveryBit = usedMask.CurrentValue & ~initialUsedMask.CurrentValue;
                if (initialChallengeId is null)
                {
                    return new ExecutionResult<bool>(false, false);
                }

                if (consumedRecoveryBit != 0)
                {
                    var userId = user.Id;
                    var recoveryCodeWasConsumed = await db.Users
                        .AsNoTracking()
                        .AnyAsync(
                            u => u.Id == userId
                                && (u.TotpRecoveryCodesUsedMask & consumedRecoveryBit) != 0,
                            verificationCt)
                        .ConfigureAwait(false);
                    var challengeWasConsumed = await db.TotpChallenges
                        .AsNoTracking()
                        .AnyAsync(
                            challenge => challenge.Id == initialChallengeId
                                && challenge.ConsumedAt != null,
                            verificationCt)
                        .ConfigureAwait(false);
                    var commitSucceeded = recoveryCodeWasConsumed && challengeWasConsumed;
                    return new ExecutionResult<bool>(commitSucceeded, commitSucceeded);
                }

                if (initialFailedAttempts is null
                    || initialFailedAttempts >= TotpChallenge.MaxFailedAttempts)
                {
                    return new ExecutionResult<bool>(false, false);
                }

                var failedAttemptWasCommitted = await db.TotpChallenges
                    .AsNoTracking()
                    .AnyAsync(
                        challenge => challenge.Id == initialChallengeId
                            && challenge.FailedAttempts == initialFailedAttempts + 1,
                        verificationCt)
                    .ConfigureAwait(false);
                return new ExecutionResult<bool>(failedAttemptWasCommitted, false);
            },
            cancellationToken: ct).ConfigureAwait(false);

        static void RestoreTracked<T>(
            Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<User, T> property,
            (T Current, T Original, bool IsModified) snapshot)
        {
            property.CurrentValue = snapshot.Current;
            property.OriginalValue = snapshot.Original;
            property.IsModified = snapshot.IsModified;
        }
    }

    private async Task<bool> VerifyAndConsumeAsync(
        User user,
        string code,
        IDbContextTransaction? transaction,
        CancellationToken ct)
    {
        var valid = await TotpVerifier.VerifyAsync(user, code, codec, db, clock, ct)
            .ConfigureAwait(false);
        if (!valid)
        {
            await challenges.RegisterFailedAttemptAsync(ct).ConfigureAwait(false);
            if (transaction is not null)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            return false;
        }

        if (!await challenges.TryConsumeCurrentAsync(ct).ConfigureAwait(false))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
            }
            return false;
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        return true;
    }
}
