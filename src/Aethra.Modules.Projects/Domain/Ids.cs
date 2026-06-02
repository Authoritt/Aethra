using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Projects.Domain;

/// <summary>
/// Identificadores del aggregate <c>Project</c> y de su entidad <c>EnvironmentVariable</c>.
/// Los IDs de Template/Client/Instance viven en sus respectivos sub-namespaces.
/// </summary>
public readonly record struct ProjectId(AethraId Value)
{
    public static ProjectId New() => new(AethraId.NewId("prj"));
    public override string ToString() => Value.ToString();
}

public readonly record struct EnvVarId(AethraId Value)
{
    public static EnvVarId New() => new(AethraId.NewId("var"));
    public override string ToString() => Value.ToString();
}

public readonly record struct SecretId(AethraId Value)
{
    public static SecretId New() => new(AethraId.NewId("sec"));
    public override string ToString() => Value.ToString();
}
