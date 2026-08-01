using Aethra.Modules.Proxy.Domain.Events;
using Aethra.Shared.Kernel.Domain;
using Aethra.Shared.Kernel.Primitives;

namespace Aethra.Modules.Proxy.Domain;

/// <summary>
/// Una ruta del reverse proxy: <c>https://hostname</c> → <c>BackendUrl</c>.
/// Alimenta YARP vía <c>DatabaseProxyConfigProvider</c> en la capa Infrastructure.
/// </summary>
public sealed class Route : AggregateRoot<RouteId>
{
    public Hostname Hostname { get; private set; }

    /// <summary>
    /// Prefijo de path para el split por ruta. <c>"/"</c> = catch-all (todo el host).
    /// <c>"/api"</c> = solo paths bajo <c>/api</c>. Permite back+front en el mismo host
    /// (ej. una app con dos servicios: <c>/api</c> → backend, <c>/</c> → frontend). El más específico gana.
    /// </summary>
    public string PathPrefix { get; private set; } = "/";
    public string BackendUrl { get; private set; }
    public bool TlsEnabled { get; private set; }
    public CertificateId? CertificateId { get; private set; }
    public string? OperationalOwnerType { get; private set; }
    public string? OperationalOwnerId { get; private set; }
    public string? Origin { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Route(RouteId id, Hostname hostname, string pathPrefix, string backendUrl, bool tlsEnabled, DateTimeOffset now) : base(id)
    {
        Hostname = hostname;
        PathPrefix = pathPrefix;
        BackendUrl = backendUrl;
        TlsEnabled = tlsEnabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Route Create(Hostname hostname, string backendUrl, bool tlsEnabled, DateTimeOffset now)
        => Create(hostname, "/", backendUrl, tlsEnabled, now);

    public static Route Create(Hostname hostname, string? pathPrefix, string backendUrl, bool tlsEnabled, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(backendUrl))
        {
            throw new ArgumentException("El backend_url no puede estar vacío.", nameof(backendUrl));
        }
        if (!EsBackendHttpValido(backendUrl))
        {
            throw new ArgumentException("backend_url debe ser una URL absoluta http(s)://...", nameof(backendUrl));
        }

        var route = new Route(RouteId.New(), hostname, NormalizePathPrefix(pathPrefix), backendUrl.Trim(), tlsEnabled, now);
        route.Raise(new RouteAddedEvent(route.Id, hostname.Value, route.BackendUrl, tlsEnabled));
        return route;
    }

    public void SetOperationalOwner(string? ownerType, string? ownerId, string? origin, DateTimeOffset now)
    {
        OperationalOwnerType = NormalizeOptional(ownerType);
        OperationalOwnerId = NormalizeOptional(ownerId);
        Origin = NormalizeOptional(origin);
        UpdatedAt = now;
    }

    /// <summary>
    /// Normaliza el prefijo: vacío/"/" → <c>"/"</c> (catch-all); si no, garantiza barra inicial
    /// y quita la barra final. Ej.: <c>"api"</c>→<c>"/api"</c>, <c>"/api/"</c>→<c>"/api"</c>.
    /// </summary>
    public static string NormalizePathPrefix(string? pathPrefix)
    {
        var p = pathPrefix?.Trim();
        if (string.IsNullOrEmpty(p) || p == "/")
        {
            return "/";
        }
        if (!p.StartsWith('/'))
        {
            p = "/" + p;
        }
        // Quita la barra final, pero un prefijo de puras barras ("//", "///") colapsa a "/"
        // (catch-all), nunca a cadena vacía — que rompería el match de path en YARP.
        var trimmed = p.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    public void UpdateBackend(string backendUrl, DateTimeOffset now)
    {
        if (!EsBackendHttpValido(backendUrl))
        {
            throw new ArgumentException("backend_url debe ser una URL absoluta http(s)://...", nameof(backendUrl));
        }
        BackendUrl = backendUrl.Trim();
        UpdatedAt = now;
        Raise(new RouteUpdatedEvent(Id, Hostname.Value, BackendUrl, TlsEnabled));
    }

    public void SetTls(bool enabled, CertificateId? certificateId, DateTimeOffset now)
    {
        TlsEnabled = enabled;
        CertificateId = enabled ? certificateId : null;
        UpdatedAt = now;
        Raise(new RouteUpdatedEvent(Id, Hostname.Value, BackendUrl, enabled));
    }

    public void MarkRemoved()
    {
        Raise(new RouteRemovedEvent(Id, Hostname.Value));
    }

    // EF Core
    private Route() : base() { BackendUrl = string.Empty; }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary>
    /// El backend de una ruta tiene que ser http o https, no solo "absoluta".
    ///
    /// El chequeo anterior era <c>Uri.TryCreate(..., UriKind.Absolute)</c> a secas, que en
    /// Windows rechaza "/relative/path" y en Linux lo ACEPTA: Unix lo interpreta como ruta
    /// absoluta de fichero y lo convierte en file:///relative/path. Aethra corre en Linux,
    /// asi que en produccion se podia apuntar el backend de una ruta a file://, ftp:// o
    /// cualquier esquema — y el mensaje de error ya prometia http(s) que el codigo nunca
    /// verificaba. Lo encontro el CI en su primera ejecucion (2026-08-01): dos tests verdes
    /// en Windows, rojos en ubuntu-latest.
    /// </summary>
    private static bool EsBackendHttpValido(string? backendUrl)
    {
        if (string.IsNullOrWhiteSpace(backendUrl))
        {
            return false;
        }
        return Uri.TryCreate(backendUrl.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }

}
