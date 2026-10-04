namespace Aethra.Modules.Deployments.Rollout;

/// <summary>Resultado de una resolución que no debe tumbar a quien la llama (OT-008 H1).</summary>
public sealed record SafeResolution<T>(bool Succeeded, T? Value, string? Error);

/// <summary>
/// OT-008 H1 — las vistas de solo lectura resuelven la infra por VM; un fallo al resolver UNA VM
/// (p. ej. un token que no se descifra) no puede dar 500 a todas: esa VM cae a la infra central.
/// La cancelación real SÍ se propaga.
/// </summary>
public static class SafeResolveRules
{
    public static async Task<SafeResolution<T>> TryResolveAsync<T>(Func<Task<T>> resolve, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        try
        {
            return new SafeResolution<T>(true, await resolve().ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SafeResolution<T>(false, default, ex.Message);
        }
    }

    /// <summary>
    /// OT-008 H-T8-1/3 — motivo por el que una vista cae a la infra central: la resolución lanzó
    /// (<paramref name="succeeded"/> false) o devolvió un bloqueo (<paramref name="blockedMessage"/>).
    /// null = se resolvió bien y no hay nada que avisar. Nunca se descarta en silencio.
    /// </summary>
    public static string? FallbackReason(bool succeeded, string? error, string? blockedMessage)
    {
        if (!succeeded)
        {
            return string.IsNullOrWhiteSpace(error) ? "resolucion fallida sin detalle" : error;
        }

        return string.IsNullOrWhiteSpace(blockedMessage) ? null : blockedMessage;
    }
}
