namespace Aethra.Modules.Notes.Domain;

/// <summary>
/// Scope polimórfico de una <see cref="Note"/> o <see cref="PinnedFact"/>.
///
/// La misma tabla almacena entidades para Project, Environment y Application — se distingue
/// por la pareja (<see cref="NoteScopeType"/>, <c>ScopeId</c>). Mismo patrón que
/// <c>Aethra.Modules.Projects.Domain.EnvVars.EnvScopeType</c>.
/// </summary>
public enum NoteScopeType
{
    Project = 0,
    Environment = 1,
    Application = 2,
}
