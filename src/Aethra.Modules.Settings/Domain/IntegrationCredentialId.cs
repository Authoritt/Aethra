using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Settings.Domain;

/// <summary>
/// Identificador de una <see cref="IntegrationCredential"/>. Prefijo estable <c>int</c>
/// — facilita que humanos o agentes IA distingan el tipo de recurso a simple vista.
/// </summary>
public readonly record struct IntegrationCredentialId(AethraId Value)
{
    public static IntegrationCredentialId New() => new(AethraId.NewId("int"));
    public override string ToString() => Value.ToString();
}
