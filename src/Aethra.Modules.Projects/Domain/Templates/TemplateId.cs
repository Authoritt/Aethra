using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Projects.Domain.Templates;

/// <summary>
/// Identificador de un <see cref="Template"/>. Prefijo <c>tpl</c>.
/// </summary>
public readonly record struct TemplateId(AethraId Value)
{
    public static TemplateId New() => new(AethraId.NewId("tpl"));
    public override string ToString() => Value.ToString();
}
