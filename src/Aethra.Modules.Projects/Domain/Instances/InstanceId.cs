using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Projects.Domain.Instances;

/// <summary>
/// Identificador de una <see cref="Instance"/>. Prefijo <c>ins</c>.
/// </summary>
public readonly record struct InstanceId(AethraId Value)
{
    public static InstanceId New() => new(AethraId.NewId("ins"));
    public override string ToString() => Value.ToString();
}
