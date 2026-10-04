namespace Aethra.Modules.Projects.Domain.Instances;

/// <summary>
/// OT-009 — hostname público de UN servicio del template fijado en una <see cref="Instance"/>
/// concreta. Gana sobre <c>TemplateService.Hostname</c> (y su token <c>{domain}</c>): permite que
/// staging publique <c>api</c> en <c>pruebas-api.inonetickets.lat</c> (un nivel, dentro del
/// certificado Universal <c>*.inonetickets.lat</c>) en vez de <c>api.pruebas.inonetickets.lat</c>.
///
/// Clase (no record) con <see cref="Hostname"/> mutable: EF actualiza la fila en sitio al cambiar el
/// host, en vez de borrar e insertar la misma clave del owned type.
/// </summary>
public sealed class ServiceHostnameOverride
{
    /// <summary>Nombre del servicio tal como lo declara el template (canónico).</summary>
    public string ServiceName { get; private set; }

    /// <summary>FQDN normalizado (minúsculas, validado con <c>Hostname.Create</c> aguas arriba).</summary>
    public string Hostname { get; private set; }

    internal ServiceHostnameOverride(string serviceName, string hostname)
    {
        ServiceName = serviceName;
        Hostname = hostname;
    }

    internal void ChangeHostname(string hostname) => Hostname = hostname;

    // EF Core
    private ServiceHostnameOverride()
    {
        ServiceName = string.Empty;
        Hostname = string.Empty;
    }
}
