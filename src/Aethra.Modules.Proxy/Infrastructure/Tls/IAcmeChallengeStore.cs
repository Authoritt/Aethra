namespace Aethra.Modules.Proxy.Infrastructure.Tls;

/// <summary>
/// Store efímero para los desafíos HTTP-01 de ACME. El <c>LetsEncryptCertManager</c> guarda el par
/// <c>(token, keyAuth)</c> antes de pedirle a la CA que valide, y el endpoint
/// <c>/.well-known/acme-challenge/{token}</c> los lee anónimamente.
///
/// La implementación de F3 vive en memoria: el challenge dura segundos y la API es single-node.
/// Si en F5+ corremos múltiples réplicas, esta interfaz permite cambiar a Redis sin tocar al manager.
/// </summary>
public interface IAcmeChallengeStore
{
    void Set(string token, string keyAuthorization);

    /// <summary>Devuelve el <c>keyAuthorization</c> si existe, sino <c>null</c>.</summary>
    string? Get(string token);

    void Remove(string token);
}
