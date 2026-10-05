using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Aethra.Modules.Deployments.Webhooks;

/// <summary>
/// OT-012 (H3 de OT-011) — anti-replay del webhook de git.
///
/// La firma de GitHub (<c>X-Hub-Signature-256</c>) cubre SOLO el cuerpo: no lleva timestamp ni
/// nonce, asi que un cuerpo firmado capturado se podia reenviar sin limite (redeploys, rollback a
/// un SHA viejo). Se recuerda cada entrega aceptada por dos claves durante <see cref="Ttl"/>:
/// <list type="bullet">
/// <item><c>X-GitHub-Delivery</c> (lo que pide la OT); pero esa cabecera NO esta firmada — quien
/// reenvia puede inventarse otra.</item>
/// <item>El SHA-256 del cuerpo firmado, que es lo que el atacante NO puede cambiar sin romper la
/// firma. Dos pushes legitimos distintos nunca tienen el mismo cuerpo (after/head_commit cambian).</item>
/// </list>
/// Se registra solo DESPUES de validar la firma (un anonimo no puede envenenar el registro con
/// ids de entregas reales). En memoria: el central es una sola instancia; un reinicio vacia el
/// registro (limite conocido, ver acta de OT-012). Coste: un "Redeliver" manual de GitHub de una
/// entrega YA aceptada dentro del TTL se rechaza (409) — para redesplegar a mano esta el
/// endpoint deploy-native.
/// </summary>
public sealed class WebhookReplayGuard
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(72);
    private const int MaxEntries = 50_000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly object _pruneLock = new();

    public WebhookReplayGuard(TimeProvider time) : this(time, DefaultTtl)
    {
    }

    public WebhookReplayGuard(TimeProvider time, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        Ttl = ttl;
    }

    public TimeSpan Ttl { get; }

    /// <summary>
    /// Registra la entrega. <c>false</c> si su id o su cuerpo ya se aceptaron dentro del TTL
    /// (replay). Atomico frente a dos entregas iguales concurrentes.
    /// </summary>
    public bool TryRegister(string? deliveryId, ReadOnlySpan<byte> body)
    {
        var now = _time.GetUtcNow();
        Prune(now);
        var keys = KeysFor(deliveryId, body);
        lock (_pruneLock)
        {
            foreach (var k in keys)
            {
                if (_seen.TryGetValue(k, out var expires) && expires > now)
                {
                    return false;
                }
            }
            foreach (var k in keys)
            {
                _seen[k] = now + Ttl;
            }
        }
        return true;
    }

    /// <summary>
    /// Olvida una entrega aceptada cuyo procesamiento fallo, para que el reintento de GitHub
    /// (mismo id y cuerpo) no se trate como replay.
    /// </summary>
    public void Forget(string? deliveryId, ReadOnlySpan<byte> body)
    {
        foreach (var k in KeysFor(deliveryId, body))
        {
            _seen.TryRemove(k, out _);
        }
    }

    private static List<string> KeysFor(string? deliveryId, ReadOnlySpan<byte> body)
    {
        var keys = new List<string>(2) { "body:" + Convert.ToHexString(SHA256.HashData(body)) };
        if (!string.IsNullOrWhiteSpace(deliveryId))
        {
            keys.Add("delivery:" + deliveryId.Trim());
        }
        return keys;
    }

    private void Prune(DateTimeOffset now)
    {
        if (_seen.Count < MaxEntries / 2)
        {
            // Barato: solo se barre cuando el registro crece.
            return;
        }
        lock (_pruneLock)
        {
            foreach (var kv in _seen)
            {
                if (kv.Value <= now)
                {
                    _seen.TryRemove(kv.Key, out _);
                }
            }
            if (_seen.Count >= MaxEntries)
            {
                // Tope duro de memoria: se descartan las que antes caducan.
                foreach (var kv in _seen.OrderBy(kv => kv.Value).Take(_seen.Count - (MaxEntries / 2)))
                {
                    _seen.TryRemove(kv.Key, out _);
                }
            }
        }
    }
}
