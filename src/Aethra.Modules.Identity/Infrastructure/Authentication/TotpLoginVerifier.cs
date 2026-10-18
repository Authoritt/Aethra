using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Domain.Totp;
using Aethra.Modules.Identity.UseCases.Totp;
using Aethra.Shared.Kernel.Time;
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
        await using IDbContextTransaction? transaction = isRecoveryCode
            ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

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
