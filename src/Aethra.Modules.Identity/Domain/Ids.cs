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
