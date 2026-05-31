using System.Security.Cryptography;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Genera contraseñas aleatorias para bindings con un alfabeto sin caracteres ambiguos
/// (sin 0/O/o/I/l/1). El password debe poder pegarse en una connection string sin escape
/// extra, así que evitamos también <c>'"\:@/?#%</c> y backtick.
/// </summary>
public static class CredentialsGenerator
{
    private const string Alphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZ" +
        "abcdefghijkmnpqrstuvwxyz" +
        "23456789" +
        "-_.+";

    public static string GeneratePassword(int length = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        Span<char> buffer = stackalloc char[length];
        for (int i = 0; i < length; i++)
        {
            // RandomNumberGenerator.GetInt32 garantiza distribución uniforme sin sesgo modular.
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(buffer);
    }
}
