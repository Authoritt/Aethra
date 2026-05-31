using System.Text.RegularExpressions;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Shared.Kernel.Primitives;

/// <summary>
/// Nombre válido de contenedor Docker: <c>[a-zA-Z0-9][a-zA-Z0-9_.-]*</c>, hasta 253 caracteres.
/// </summary>
public readonly partial record struct ContainerName
{
    public string Value { get; }

    private ContainerName(string value)
    {
        Value = value;
    }

    public override string ToString() => Value;

    public static Result<ContainerName> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("container.empty", "El nombre del contenedor no puede estar vacío.");
        }
        var v = input.Trim();
        if (v.Length > 253)
        {
            return Error.Validation("container.length", "El nombre del contenedor no puede exceder 253 caracteres.");
        }
        if (!ContainerNameRegex().IsMatch(v))
        {
            return Error.Validation(
                "container.format",
                "Formato inválido. Use solo letras, dígitos, '.', '_' y '-'. Debe empezar con letra o dígito.");
        }
        return new ContainerName(v);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-]{0,252}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContainerNameRegex();
}
