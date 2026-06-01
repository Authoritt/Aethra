using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Settings.Domain;

/// <summary>
/// Identificador de una <see cref="EnvironmentDefinition"/>. Prefijo estable <c>envd</c>.
/// </summary>
public readonly record struct EnvironmentDefinitionId(AethraId Value)
{
    public static EnvironmentDefinitionId New() => new(AethraId.NewId("envd"));
    public override string ToString() => Value.ToString();
}
