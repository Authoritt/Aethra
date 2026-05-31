using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Vms.Domain;

public readonly record struct VmId(AethraId Value)
{
    public static VmId New() => new(AethraId.NewId("vm"));
    public override string ToString() => Value.ToString();
}

public readonly record struct SatelliteId(AethraId Value)
{
    public static SatelliteId New() => new(AethraId.NewId("sat"));
    public override string ToString() => Value.ToString();
}
