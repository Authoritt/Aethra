using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Deployments.Domain.Deployment;

/// <summary>
/// Identidad del agregado <see cref="Deployment"/>. Prefijo <c>dep</c>.
/// </summary>
public readonly record struct DeploymentId(AethraId Value)
{
    public static DeploymentId New() => new(AethraId.NewId("dep"));
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identidad de cada línea append-only de <see cref="DeploymentLogEntry"/>. Prefijo <c>deplog</c>.
/// </summary>
public readonly record struct DeploymentLogId(AethraId Value)
{
    public static DeploymentLogId New() => new(AethraId.NewId("deplog"));
    public override string ToString() => Value.ToString();
}
