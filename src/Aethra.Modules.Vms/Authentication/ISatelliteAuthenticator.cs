using Aethra.Modules.Vms.Domain;

namespace Aethra.Modules.Vms.Authentication;

/// <summary>
/// Verifica un token presentado por un satélite y devuelve el <see cref="VmId"/> asociado.
/// Implementación EF: query por hash del token (índice) y comparación constant-time del hash.
/// </summary>
public interface ISatelliteAuthenticator
{
    Task<VmId?> AuthenticateAsync(string presentedToken, CancellationToken ct);
}
