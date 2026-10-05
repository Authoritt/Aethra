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
/// Ambas claves van ACOTADAS al Template autenticado (G2 de OT-012): dos Templates en el mismo
/// repo+rama son dos webhooks en GitHub con el MISMO cuerpo (distinto secreto y distinto id); una
/// clave global por cuerpo haria que el segundo recibiera 409 y su deploy se perdiera.
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
    public bool TryRegister(string scope, string? deliveryId, ReadOnlySpan<byte> body)
    {
        var now = _time.GetUtcNow();
        Prune(now);
        var keys = KeysFor(scope, deliveryId, body);
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
    public void Forget(string scope, string? deliveryId, ReadOnlySpan<byte> body)
    {
        foreach (var k in KeysFor(scope, deliveryId, body))
        {
            _seen.TryRemove(k, out _);
        }
    }

    /// <param name="scope">Template autenticado por la firma (la clave nunca es global).</param>
    private static List<string> KeysFor(string scope, string? deliveryId, ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var keys = new List<string>(2) { $"{scope}|body:" + Convert.ToHexString(SHA256.HashData(body)) };
        if (!string.IsNullOrWhiteSpace(deliveryId))
        {
            keys.Add($"{scope}|delivery:" + deliveryId.Trim());
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
