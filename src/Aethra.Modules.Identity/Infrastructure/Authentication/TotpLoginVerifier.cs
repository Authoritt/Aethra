using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.UseCases.Totp;
using Aethra.Shared.Kernel.Time;

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
    IClock clock) : ITotpLoginVerifier
{
    public Task<bool> VerifyAsync(User user, string code, CancellationToken ct)
        => TotpVerifier.VerifyAsync(user, code, codec, db, clock, ct);
}
