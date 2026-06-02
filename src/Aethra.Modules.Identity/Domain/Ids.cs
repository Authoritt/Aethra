using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Identity.Domain;

/// <summary>
/// Identificador de una <see cref="ApiKey"/>. Prefijo estable <c>apk</c> — facilita
/// que humanos o agentes IA distingan el tipo de recurso a simple vista, igual que
/// los prefijos de Stripe (<c>cus_</c>, <c>ch_</c>).
/// </summary>
public readonly record struct ApiKeyId(AethraId Value)
{
    public static ApiKeyId New() => new(AethraId.NewId("apk"));
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identificador de un <see cref="User"/>. Prefijo estable <c>usr</c>.
/// </summary>
public readonly record struct UserId(AethraId Value)
{
    public static UserId New() => new(AethraId.NewId("usr"));
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identificador de un <see cref="Role"/>. Prefijo estable <c>rol</c>.
/// </summary>
public readonly record struct RoleId(AethraId Value)
{
    public static RoleId New() => new(AethraId.NewId("rol"));
    public override string ToString() => Value.ToString();
}
