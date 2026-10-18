using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Identity.Infrastructure;

/// <summary>
/// Almacen de un unico usuario admin durante F0 — sin BD.
/// Permite verificar credenciales contra el hash en memoria.
/// </summary>
public sealed class SingleUserStore
{
    public string AdminEmail { get; }
    private readonly string _hash;

    public SingleUserStore(IOptions<IdentityOptions> options)
    {
        var opts = options.Value;
        AdminEmail = opts.AdminEmail;

        if (!string.IsNullOrWhiteSpace(opts.AdminPasswordHash))
        {
            _hash = opts.AdminPasswordHash;
        }
        else if (!string.IsNullOrWhiteSpace(opts.AdminPasswordSeed))
        {
            _hash = PasswordHasher.Hash(opts.AdminPasswordSeed);
        }
        else
        {
            // F0: cuando no hay credencial configurada, se acepta una por defecto SOLO en Development.
            // El host loguea una advertencia visible al arrancar.
            _hash = PasswordHasher.Hash("aethra-dev");
        }
    }

    public bool ValidateCredentials(string email, string password)
    {
        if (!string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return PasswordHasher.Verify(password, _hash);
    }

    public async ValueTask<bool> ValidateCredentialsAsync(
        string? email,
        string password,
        IAuthenticationAttemptLimiter attemptLimiter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attemptLimiter);

        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var canonicalAdminEmail = User.NormalizeEmail(AdminEmail);
        if (!string.Equals(User.NormalizeEmail(email), canonicalAdminEmail, StringComparison.Ordinal))
        {
            return false;
        }

        var lease = await attemptLimiter.AcquirePasswordAsync(canonicalAdminEmail, cancellationToken);
        if (!lease.IsAllowed || !PasswordHasher.Verify(password, _hash))
        {
            return false;
        }

        await attemptLimiter.ResetPasswordAsync(canonicalAdminEmail, cancellationToken);
        return true;
    }
}
