using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Aethra.Modules.Identity.Domain;

/// <summary>
/// Argon2id con parametros conservadores para login interactivo en hardware modesto.
/// Salt aleatorio de 16 bytes; hash de 32 bytes.
/// Formato persistido: <c>$argon2id$v=19$m=65536,t=4,p=2$&lt;salt-b64&gt;$&lt;hash-b64&gt;</c>.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKb = 65536;
    private const int Iterations = 4;
    private const int Parallelism = 2;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = ComputeHash(password, salt);
        return $"$argon2id$v=19$m={MemoryKb},t={Iterations},p={Parallelism}$"
            + $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "argon2id")
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = ComputeHash(password, salt);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] ComputeHash(string password, byte[] salt)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = Parallelism,
            MemorySize = MemoryKb,
            Iterations = Iterations,
        };
        return argon.GetBytes(HashSize);
    }
}
