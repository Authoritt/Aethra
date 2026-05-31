using System.Text.RegularExpressions;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Shared.Kernel.Primitives;

/// <summary>
/// SHA-1 hex de Git (40 caracteres) o forma corta (7..39 caracteres). Lowercase.
/// </summary>
public readonly partial record struct GitSha
{
    public string Value { get; }

    private GitSha(string value)
    {
        Value = value;
    }

    public override string ToString() => Value;

    /// <summary>
    /// Forma abreviada de 7 caracteres (convención Git para SHA-1 corto).
    /// </summary>
    public string Abbreviated => Value.Length >= 7 ? Value[..7] : Value;

    public static Result<GitSha> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("sha.empty", "El SHA no puede estar vacío.");
        }
        var v = input.Trim().ToLowerInvariant();
        if (!ShaRegex().IsMatch(v))
        {
            return Error.Validation(
                "sha.format",
                "SHA inválido. Debe ser hex de 7 a 40 caracteres.");
        }
        return new GitSha(v);
    }

    [GeneratedRegex("^[0-9a-f]{7,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShaRegex();
}
