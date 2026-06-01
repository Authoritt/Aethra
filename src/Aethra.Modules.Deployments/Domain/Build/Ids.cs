using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Deployments.Domain.Build;

/// <summary>
/// Identidad del agregado <see cref="Build"/>. Prefijo <c>bld</c>.
/// </summary>
public readonly record struct BuildId(AethraId Value)
{
    public static BuildId New() => new(AethraId.NewId("bld"));
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identidad de cada línea append-only de <see cref="BuildLogEntry"/>. Prefijo <c>bldlog</c>.
/// </summary>
public readonly record struct BuildLogId(AethraId Value)
{
    public static BuildLogId New() => new(AethraId.NewId("bldlog"));
    public override string ToString() => Value.ToString();
}
