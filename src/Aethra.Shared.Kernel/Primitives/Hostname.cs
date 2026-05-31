using System.Text.RegularExpressions;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Shared.Kernel.Primitives;

/// <summary>
/// FQDN válido según RFC 1123: labels alfanuméricas separadas por puntos, 1..253 caracteres total.
/// Acepta también valores con asterisco para wildcards (ej. <c>*.example.com</c>) usados por Cloudflare.
/// </summary>
public readonly partial record struct Hostname
{
    public string Value { get; }

    private Hostname(string value)
    {
        Value = value;
    }

    public override string ToString() => Value;

    public bool IsWildcard => Value.StartsWith("*.", StringComparison.Ordinal);

    public static Result<Hostname> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("hostname.empty", "El hostname no puede estar vacío.");
        }
        var v = input.Trim().ToLowerInvariant();
        if (v.Length > 253)
        {
            return Error.Validation("hostname.length", "El hostname no puede exceder 253 caracteres.");
        }
        if (!HostnameRegex().IsMatch(v))
        {
            return Error.Validation(
                "hostname.format",
                "Hostname inválido. Debe ser FQDN tipo 'sub.dominio.com' (acepta '*' como subdominio inicial).");
        }
        return new Hostname(v);
    }

    [GeneratedRegex(@"^(\*\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnameRegex();
}
