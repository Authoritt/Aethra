using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Domain.Events;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Proxy.Tests;

/// <summary>
/// Invariantes del agregado <see cref="Route"/> (reverse proxy → YARP): normalización del
/// PathPrefix (incl. la regresión de prefijos de puras barras), validación de backend URL,
/// y el comportamiento de TLS (deshabilitar limpia el certificado).
/// </summary>
public sealed class RouteTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Hostname Host => Hostname.Create("app.example.com").Value;

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("/", "/")]
    [InlineData("api", "/api")]
    [InlineData("/api", "/api")]
    [InlineData("/api/", "/api")]
    [InlineData("/api//", "/api")]
    [InlineData("api/", "/api")]
    [InlineData("/a/b", "/a/b")]
    [InlineData("//", "/")]   // regresión: puras barras → "/" (catch-all), nunca ""
    [InlineData("///", "/")]
    public void NormalizePathPrefix_normalizes_to_a_canonical_prefix(string? input, string expected)
    {
        Route.NormalizePathPrefix(input).Should().Be(expected);
    }

    [Fact]
    public void Create_defaults_path_prefix_to_root_and_raises_event()
    {
        var route = Route.Create(Host, "https://backend:8080", tlsEnabled: false, Now);

        route.PathPrefix.Should().Be("/");
        route.BackendUrl.Should().Be("https://backend:8080");
        route.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<RouteAddedEvent>();
    }

    [Fact]
    public void Create_normalizes_the_supplied_path_prefix()
    {
        var route = Route.Create(Host, "api/", "https://backend:8080", tlsEnabled: false, Now);

        route.PathPrefix.Should().Be("/api");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    // En Linux, Uri.TryCreate(UriKind.Absolute) ACEPTA esto como file:///relative/path,
    // y en Windows lo rechaza. El CI lo destapó: verde en local, rojo en ubuntu-latest.
    [InlineData("/relative/path")]
    // Los esquemas que el mensaje de error siempre prometió rechazar y el código no
    // comprobaba. Un backend de proxy apuntando a file:// no es un caso teórico:
    // esta plataforma corre en Linux y enruta tráfico de producción.
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://interno/algo")]
    [InlineData("gopher://qué/hace/esto/aquí")]
    public void Create_throws_on_invalid_backend_url(string backendUrl)
    {
        var act = () => Route.Create(Host, backendUrl, tlsEnabled: false, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("http://backend:8080")]
    [InlineData("https://backend.example.com")]
    [InlineData("  https://con-espacios:5080  ")]
    public void Create_accepts_http_and_https(string backendUrl)
    {
        var route = Route.Create(Host, backendUrl, tlsEnabled: false, Now);

        route.BackendUrl.Should().Be(backendUrl.Trim());
    }

    [Theory]
    [InlineData("/relative/path")]
    [InlineData("file:///etc/passwd")]
    public void UpdateBackend_throws_on_non_http_schemes(string backendUrl)
    {
        var route = Route.Create(Host, "https://b:1", tlsEnabled: false, Now);

        var act = () => route.UpdateBackend(backendUrl, Now);

        act.Should().Throw<ArgumentException>();
        route.BackendUrl.Should().Be("https://b:1");
    }

    [Fact]
    public void SetTls_enables_and_stores_the_certificate()
    {
        var route = Route.Create(Host, "https://b:1", tlsEnabled: false, Now);
        var certId = CertificateId.New();

        route.SetTls(true, certId, Now.AddMinutes(1));

        route.TlsEnabled.Should().BeTrue();
        route.CertificateId.Should().Be(certId);
    }

    [Fact]
    public void SetTls_disabling_clears_the_certificate_even_if_one_is_passed()
    {
        var route = Route.Create(Host, "https://b:1", tlsEnabled: false, Now);
        route.SetTls(true, CertificateId.New(), Now);

        route.SetTls(false, CertificateId.New(), Now.AddMinutes(1));

        route.TlsEnabled.Should().BeFalse();
        route.CertificateId.Should().BeNull();
    }

    [Fact]
    public void UpdateBackend_changes_url_and_raises_event()
    {
        var route = Route.Create(Host, "https://old:1", tlsEnabled: false, Now);
        route.ClearDomainEvents();

        route.UpdateBackend("https://new:2", Now.AddMinutes(1));

        route.BackendUrl.Should().Be("https://new:2");
        route.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<RouteUpdatedEvent>();
    }

    [Fact]
    public void UpdateBackend_throws_on_relative_url()
    {
        var route = Route.Create(Host, "https://old:1", tlsEnabled: false, Now);

        var act = () => route.UpdateBackend("/relative", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkRemoved_raises_removed_event()
    {
        var route = Route.Create(Host, "https://b:1", tlsEnabled: false, Now);
        route.ClearDomainEvents();

        route.MarkRemoved();

        route.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<RouteRemovedEvent>();
    }
}
