using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Settings.Domain;

/// <summary>
/// Identificador de un <see cref="BaseDomain"/>. Prefijo estable <c>bd</c>.
/// </summary>
public readonly record struct BaseDomainId(AethraId Value)
{
    public static BaseDomainId New() => new(AethraId.NewId("bd"));
    public override string ToString() => Value.ToString();
}
