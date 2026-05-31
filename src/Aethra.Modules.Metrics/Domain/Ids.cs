using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Metrics.Domain;

public readonly record struct VmMetricId(AethraId Value)
{
    public static VmMetricId New() => new(AethraId.NewId("vmm"));
    public override string ToString() => Value.ToString();
}

public readonly record struct ContainerSnapshotId(AethraId Value)
{
    public static ContainerSnapshotId New() => new(AethraId.NewId("cs"));
    public override string ToString() => Value.ToString();
}
