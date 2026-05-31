using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Monitoring.Domain;

/// <summary>
/// Identificador de un <see cref="Monitor"/>. Prefijo <c>mon_</c>.
/// </summary>
public readonly record struct MonitorId(AethraId Value)
{
    public static MonitorId New() => new(AethraId.NewId("mon"));
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identificador de un <see cref="MonitorCheck"/>. Prefijo <c>chk_</c>.
/// </summary>
public readonly record struct MonitorCheckId(AethraId Value)
{
    public static MonitorCheckId New() => new(AethraId.NewId("chk"));
    public override string ToString() => Value.ToString();
}
