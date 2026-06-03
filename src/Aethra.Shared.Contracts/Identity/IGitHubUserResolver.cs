namespace Aethra.Shared.Contracts.Identity;

/// <summary>
/// F12.3 — Read-model cross-module: resuelve un <c>UserId</c> Aethra a partir del handle de
/// GitHub. Permite que el módulo <c>Deployments</c> (webhook handler) consulte el módulo
/// <c>Identity</c> sin referenciar sus internals.
///
/// Caso de uso: cuando llega un <c>pull_request.opened</c>, el webhook handler debe:
/// 1) leer <c>pull_request.user.login</c>,
/// 2) llamar a <see cref="FindByGitHubUsernameAsync"/> para obtener el <c>UserId</c>,
/// 3) si null → postear comment "Configura tu GitHub username en Aethra" + skip,
/// 4) si OK → setear <c>Instance.CreatedByUserId = userId</c>.
/// </summary>
public interface IGitHubUserResolver
{
    /// <summary>
    /// Devuelve el <c>UserId</c> Aethra cuyo <c>GitHubUsername</c> matchea (case-insensitive).
    /// <c>null</c> si nadie configuró ese handle. <c>null</c> también para handle vacío/whitespace.
    /// </summary>
    Task<string?> FindByGitHubUsernameAsync(string gitHubUsername, CancellationToken ct);
}
