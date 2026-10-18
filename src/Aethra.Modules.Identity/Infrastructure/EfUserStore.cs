using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Kernel.Time;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Identity.Infrastructure;

/// <summary>
/// Wrapper sobre <see cref="IUserRepository"/> + <see cref="IUserPasswordCodec"/> que valida
/// credenciales contra la BD. El handler de login lo invoca PRIMERO; si la tabla users
/// está vacía (bootstrap inicial) cae de vuelta al <see cref="SingleUserStore"/>.
///
/// El método mantiene un patrón fail-fast: rechaza users inactivos, normaliza el email
/// y desofusca el hash exactamente una vez por intento. Si el cifrado de DataProtection
/// está corrupto (rotación accidental sin migrar), <see cref="IUserPasswordCodec.Verify"/>
/// devuelve false sin lanzar — fail-secure.
/// </summary>
public sealed class EfUserStore(
    IUserRepository users,
    IUserPasswordCodec passwords,
    IClock clock,
    IAuthenticationAttemptLimiter attemptLimiter,
    ILogger<EfUserStore> logger)
{
    public sealed record AuthenticatedUser(User User, IReadOnlyList<string> RoleSlugs);

    public async Task<User?> ValidateCredentialsAsync(string email, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var user = await users.FindByEmailAsync(email, ct).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }
        if (!user.IsActive)
        {
            return null;
        }
        var lease = await attemptLimiter.AcquirePasswordAsync(email, ct).ConfigureAwait(false);
        if (!lease.IsAllowed)
        {
            logger.LogWarning(
                "Login rechazado antes de verificar password por bloqueo temporal hasta {ExpiresAt}",
                lease.ExpiresAt);
            return null;
        }
        if (!passwords.Verify(password, user.PasswordHashCipher))
        {
            return null;
        }
        // El primer factor no completa el login cuando TOTP esta habilitado. El endpoint
        // /auth/login/totp marca el login solo despues de verificar y consumir el challenge.
        if (!user.TotpEnabled)
        {
            user.MarkLogin(clock.UtcNow);
        }
        await attemptLimiter.ResetPasswordAsync(email, ct).ConfigureAwait(false);
        return user;
    }

    public Task<int> CountAsync(CancellationToken ct) => users.CountAsync(ct);
}
