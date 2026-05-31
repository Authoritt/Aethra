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
    public string BackendUrl { get; private set; }
    public bool TlsEnabled { get; private set; }
    public CertificateId? CertificateId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Route(RouteId id, Hostname hostname, string backendUrl, bool tlsEnabled, DateTimeOffset now) : base(id)
    {
        Hostname = hostname;
        BackendUrl = backendUrl;
        TlsEnabled = tlsEnabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Route Create(Hostname hostname, string backendUrl, bool tlsEnabled, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(backendUrl))
        {
            throw new ArgumentException("El backend_url no puede estar vacío.", nameof(backendUrl));
        }
        if (!Uri.TryCreate(backendUrl.Trim(), UriKind.Absolute, out _))
        {
            throw new ArgumentException("backend_url debe ser una URL absoluta http(s)://...", nameof(backendUrl));
        }

        var route = new Route(RouteId.New(), hostname, backendUrl.Trim(), tlsEnabled, now);
        route.Raise(new RouteAddedEvent(route.Id, hostname.Value, route.BackendUrl, tlsEnabled));
        return route;
    }

    public void UpdateBackend(string backendUrl, DateTimeOffset now)
    {
        if (!Uri.TryCreate(backendUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("backend_url debe ser una URL absoluta.", nameof(backendUrl));
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
}
