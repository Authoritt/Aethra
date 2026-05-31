namespace Aethra.Modules.Projects.Domain.EnvVars;

/// <summary>
/// Scopes de resolución de env vars en el nuevo modelo Template/Client/Instance.
/// Cascada (lo más específico gana): Instance &gt; Client &gt; Template &gt; Project.
///
/// Espejo del enum cross-module <c>Aethra.Shared.Contracts.Projects.EnvVarScope</c>; los
/// valores numéricos se mantienen alineados intencionalmente por si en algún momento se
/// quiere intercambiar (cualquier cambio aquí requiere cambiar el contracts).
/// </summary>
public enum EnvScopeType
{
    Project = 0,
    Template = 1,
    Client = 2,
    Instance = 3,
}
