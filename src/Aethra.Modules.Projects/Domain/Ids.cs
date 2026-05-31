using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Projects.Domain;

public readonly record struct ProjectId(AethraId Value)
{
    public static ProjectId New() => new(AethraId.NewId("prj"));
    public override string ToString() => Value.ToString();
}

public readonly record struct EnvironmentId(AethraId Value)
{
    public static EnvironmentId New() => new(AethraId.NewId("env"));
    public override string ToString() => Value.ToString();
}

public readonly record struct ApplicationId(AethraId Value)
{
    public static ApplicationId New() => new(AethraId.NewId("app"));
    public override string ToString() => Value.ToString();
}

public readonly record struct EnvVarId(AethraId Value)
{
    public static EnvVarId New() => new(AethraId.NewId("var"));
    public override string ToString() => Value.ToString();
}
