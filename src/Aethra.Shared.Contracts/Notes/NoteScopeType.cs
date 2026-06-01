namespace Aethra.Shared.Contracts.Notes;

/// <summary>
/// Scope polimórfico de una nota o pinned-fact.
///
/// Vive en <c>Shared.Contracts</c> para que módulos externos (Mcp) puedan construir
/// comandos sin depender de <c>Aethra.Modules.Notes.Domain</c>. Mismo patrón que
/// <c>Aethra.Shared.Contracts.Projects.EnvVarScope</c>.
/// </summary>
public enum NoteScopeType
{
    Project = 0,
    Template = 1,
    Client = 2,
    Instance = 3,
}
