namespace Aethra.Modules.Notes.Domain;

/// <summary>
/// Scope polimórfico de una <see cref="Note"/> o <see cref="PinnedFact"/>.
///
/// La misma tabla almacena entidades para Project, Template, Client e Instance — se distingue
/// por la pareja (<see cref="NoteScopeType"/>, <c>ScopeId</c>). Mismo patrón que
/// <c>Aethra.Modules.Projects.Domain.EnvVars.EnvScopeType</c> y
/// <c>Aethra.Shared.Contracts.Projects.EnvVarScope</c>.
/// </summary>
public enum NoteScopeType
{
    Project = 0,
    Template = 1,
    Client = 2,
    Instance = 3,
}
