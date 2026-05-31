using System.Text.RegularExpressions;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Postgres no permite parámetros ligados para identificadores (db/role/schema). En lugar de
/// concatenar texto del usuario aceptamos solo un alfabeto restrictivo y luego envolvemos con
/// quoted identifier. Cualquier carácter fuera del set provoca <see cref="ArgumentException"/>.
/// </summary>
public static partial class PostgresIdentifier
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePattern();

    public static string Quote(string raw)
    {
        if (string.IsNullOrEmpty(raw) || !SafePattern().IsMatch(raw))
        {
            throw new ArgumentException($"Identifier '{raw}' inválido para Postgres.", nameof(raw));
        }
        return "\"" + raw + "\"";
    }
}
