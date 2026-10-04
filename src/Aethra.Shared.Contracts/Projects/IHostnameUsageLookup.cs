namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// OT-009 (G2) — qué instancias declaran un hostname como suyo (CustomDomain, AutoHostname u override
/// por servicio). Cloudflare la consulta antes de borrar el DNS de un override al borrar una instancia.
/// </summary>
public interface IHostnameUsageLookup
{
    /// <summary>IDs de las instancias (salvo <paramref name="excludingInstanceId"/>) que usan el host.</summary>
    Task<IReadOnlyList<string>> FindInstancesUsingHostnameAsync(
        string hostname, string? excludingInstanceId, CancellationToken ct);
}
