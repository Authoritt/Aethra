using System.Collections.Concurrent;

namespace Aethra.Modules.Proxy.Infrastructure.Tls;

/// <summary>
/// Implementación in-memory de <see cref="IAcmeChallengeStore"/>. Registrar como singleton:
/// el endpoint (transient/request-scoped) y el manager (scoped) deben compartir la misma instancia.
/// </summary>
public sealed class InMemoryAcmeChallengeStore : IAcmeChallengeStore
{
    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);

    public void Set(string token, string keyAuthorization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAuthorization);
        _store[token] = keyAuthorization;
    }

    public string? Get(string token)
        => string.IsNullOrEmpty(token) ? null : _store.GetValueOrDefault(token);

    public void Remove(string token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            _store.TryRemove(token, out _);
        }
    }
}
