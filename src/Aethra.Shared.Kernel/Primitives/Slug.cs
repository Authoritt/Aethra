using System.Text.RegularExpressions;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Shared.Kernel.Primitives;

/// <summary>
/// Identificador URL-friendly: kebab-case, sin diacríticos, 1..64 caracteres.
/// Ejemplos válidos: "mi-app", "backend", "proyecto-personal-2".
/// </summary>
public readonly partial record struct Slug
{
    public string Value { get; }

    private Slug(string value)
    {
        Value = value;
    }

    public override string ToString() => Value;

    public static Result<Slug> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("slug.empty", "El slug no puede estar vacío.");
        }
        var trimmed = input.Trim().ToLowerInvariant();
        if (trimmed.Length is < 1 or > 64)
        {
            return Error.Validation("slug.length", "El slug debe tener entre 1 y 64 caracteres.");
        }
        if (!SlugRegex().IsMatch(trimmed))
        {
            return Error.Validation(
                "slug.format",
                "El slug solo permite letras minúsculas, dígitos y guiones. No puede empezar ni terminar con guion.");
        }
        return new Slug(trimmed);
    }

    /// <summary>
    /// Convierte texto libre a slug eliminando diacríticos y caracteres inválidos.
    /// Útil para sugerencias desde nombres de repos o proyectos.
    /// </summary>
    public static Slug Suggest(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new Slug("app");
        }
        var normalized = input.Trim().ToLowerInvariant();
        normalized = DiacriticsRegex().Replace(normalized, m => m.Value switch
        {
            "á" or "ä" or "â" or "à" => "a",
            "é" or "ë" or "ê" or "è" => "e",
            "í" or "ï" or "î" or "ì" => "i",
            "ó" or "ö" or "ô" or "ò" => "o",
            "ú" or "ü" or "û" or "ù" => "u",
            "ñ" => "n",
            "ç" => "c",
            _ => "",
        });
        normalized = InvalidCharsRegex().Replace(normalized, "-");
        normalized = MultiDashRegex().Replace(normalized, "-");
        normalized = normalized.Trim('-');
        if (normalized.Length == 0)
        {
            normalized = "app";
        }
        if (normalized.Length > 64)
        {
            normalized = normalized[..64].TrimEnd('-');
        }
        return new Slug(normalized);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    [GeneratedRegex("[áäâàéëêèíïîìóöôòúüûùñç]", RegexOptions.CultureInvariant)]
    private static partial Regex DiacriticsRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidCharsRegex();

    [GeneratedRegex("-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiDashRegex();
}
