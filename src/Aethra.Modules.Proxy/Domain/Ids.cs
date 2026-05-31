using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Proxy.Domain;

public readonly record struct RouteId(AethraId Value)
{
    public static RouteId New() => new(AethraId.NewId("rt"));
    public override string ToString() => Value.ToString();
}

public readonly record struct CertificateId(AethraId Value)
{
    public static CertificateId New() => new(AethraId.NewId("cert"));
    public override string ToString() => Value.ToString();
}
