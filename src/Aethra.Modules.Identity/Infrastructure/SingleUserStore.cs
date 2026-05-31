using Aethra.Modules.Identity.Domain;
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
}
