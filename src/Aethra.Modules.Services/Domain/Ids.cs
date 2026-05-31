using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Services.Domain;

public readonly record struct ManagedServiceId(AethraId Value)
{
    public static ManagedServiceId New() => new(AethraId.NewId("svc"));
    public override string ToString() => Value.ToString();
}

public readonly record struct ServiceBindingId(AethraId Value)
{
    public static ServiceBindingId New() => new(AethraId.NewId("bnd"));
    public override string ToString() => Value.ToString();
}
