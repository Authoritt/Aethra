using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Deployments.Domain;

public readonly record struct DeployJobId(AethraId Value)
{
    public static DeployJobId New() => new(AethraId.NewId("dpl"));
    public override string ToString() => Value.ToString();
}

public readonly record struct DeployLogId(AethraId Value)
{
    public static DeployLogId New() => new(AethraId.NewId("dpllog"));
    public override string ToString() => Value.ToString();
}
