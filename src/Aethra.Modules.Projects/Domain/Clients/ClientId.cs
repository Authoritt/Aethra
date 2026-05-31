using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Projects.Domain.Clients;

/// <summary>
/// Identificador de un <see cref="Client"/>. Prefijo <c>cli</c>.
/// </summary>
public readonly record struct ClientId(AethraId Value)
{
    public static ClientId New() => new(AethraId.NewId("cli"));
    public override string ToString() => Value.ToString();
}
