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
}
