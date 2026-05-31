using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Shared.Kernel.Primitives;

/// <summary>
/// Puerto TCP/UDP válido (1..65535). 0 es inválido (well-known reserved).
/// </summary>
public readonly record struct Port
{
    public int Value { get; }

    private Port(int value)
    {
        Value = value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static Result<Port> Create(int input)
    {
        if (input is < 1 or > 65535)
        {
            return Error.Validation("port.range", "Puerto debe estar entre 1 y 65535.");
        }
        return new Port(input);
    }

    public static implicit operator int(Port port) => port.Value;
}
