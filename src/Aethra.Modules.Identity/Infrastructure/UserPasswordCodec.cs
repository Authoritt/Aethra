using System.Text;
using Aethra.Modules.Identity.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Aethra.Modules.Identity.Infrastructure;

/// <summary>
/// Codec del password hash de un <see cref="User"/>. Encadena dos capas:
/// <list type="number">
///   <item><see cref="PasswordHasher"/> (Argon2id) para resistir cracking offline.</item>
///   <item>ASP.NET DataProtection (purpose <c>aethra-user-passwords</c>) para
///   encrypt-at-rest del hash — si la BD se filtra, el atacante necesita además
///   las llaves de DataProtection para empezar a probar contraseñas.</item>
/// </list>
///
/// El método <see cref="VerifyAndProtect"/> desofusca el ciphertext, valida con
/// <see cref="PasswordHasher.Verify"/> y devuelve el resultado.
/// </summary>
public interface IUserPasswordCodec
{
    /// <summary>Hashea (Argon2id) y cifra (DataProtection) un password en texto plano.</summary>
    byte[] HashAndProtect(string plainPassword);

    /// <summary>True si <paramref name="plainPassword"/> coincide con el hash protegido.</summary>
    bool Verify(string plainPassword, byte[] storedCipher);
}

internal sealed class DataProtectionUserPasswordCodec : IUserPasswordCodec
{
    private const string Purpose = "aethra-user-passwords";
    private readonly IDataProtector _protector;

    public DataProtectionUserPasswordCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] HashAndProtect(string plainPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainPassword);
        var hash = PasswordHasher.Hash(plainPassword);
        var hashBytes = Encoding.UTF8.GetBytes(hash);
        return _protector.Protect(hashBytes);
    }

    public bool Verify(string plainPassword, byte[] storedCipher)
    {
        if (string.IsNullOrEmpty(plainPassword) || storedCipher is null || storedCipher.Length == 0)
        {
            return false;
        }
        try
        {
            var hashBytes = _protector.Unprotect(storedCipher);
            var hash = Encoding.UTF8.GetString(hashBytes);
            return PasswordHasher.Verify(plainPassword, hash);
        }
        catch (Exception)
        {
            // Cifrado corrupto o desprotegido con otra key — tratamos como contraseña inválida
            // para no filtrar diferencias de fallos al caller.
            return false;
        }
    }
}
