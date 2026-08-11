using Aethra.Modules.Projects.UseCases.Templates.Queries;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

/// <summary>
/// Extracción del host de una URL de repositorio, que es lo que la política de destinos autoriza
/// antes de dejar que el plano de control ejecute <c>git clone</c>.
///
/// <para>Importa que no se escape ninguna forma: el validador acepta <c>https://</c>, <c>http://</c>,
/// <c>ssh://</c>, <c>git://</c> y la forma SCP <c>git@host:ruta</c> — y esta última <b>no es una URI
/// válida</b>, así que hay que partirla a mano. Un host que no se sepa extraer no puede autorizarse:
/// devolver "no lo sé" tiene que traducirse en rechazo, no en permitir.</para>
/// </summary>
public sealed class DiscoverTemplateHostTests
{
    [Theory]
    [InlineData("https://github.com/acme/app.git", "github.com")]
    [InlineData("http://gitlab.internal/acme/app.git", "gitlab.internal")]
    [InlineData("ssh://git@github.com/acme/app.git", "github.com")]
    [InlineData("git://github.com/acme/app.git", "github.com")]
    [InlineData("https://user:pass@github.com/acme/app.git", "github.com")]
    [InlineData("https://github.com:8443/acme/app.git", "github.com")]
    public void Absolute_urls_yield_their_host(string url, string expected)
        => DiscoverTemplateHandler.ExtractHost(url).Should().Be(expected);

    /// <summary>
    /// La forma SCP no es una URI: <c>Uri.TryCreate</c> la rechaza o la interpreta mal, así que se
    /// parte por el primer <c>:</c> posterior al <c>@</c>.
    /// </summary>
    [Theory]
    [InlineData("git@github.com:acme/app.git", "github.com")]
    [InlineData("git@10.0.0.5:acme/app.git", "10.0.0.5")]
    [InlineData("deploy@gitlab.internal:group/app.git", "gitlab.internal")]
    public void Scp_style_urls_yield_their_host(string url, string expected)
        => DiscoverTemplateHandler.ExtractHost(url).Should().Be(expected);

    /// <summary>
    /// Un literal IP tiene que salir tal cual para que la política lo clasifique: escribir la
    /// dirección a mano es la forma más directa de intentar alcanzar un servicio interno.
    /// </summary>
    [Theory]
    [InlineData("https://127.0.0.1/repo.git", "127.0.0.1")]
    [InlineData("https://169.254.169.254/latest/meta-data", "169.254.169.254")]
    [InlineData("http://100.116.223.31:3000/repo.git", "100.116.223.31")]
    public void Ip_literals_are_preserved(string url, string expected)
        => DiscoverTemplateHandler.ExtractHost(url).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-una-url")]
    [InlineData("git@")]
    public void An_undeterminable_host_returns_null(string? url)
        => DiscoverTemplateHandler.ExtractHost(url).Should().BeNull();

    /// <summary>
    /// Con el usuario vacío (<c>@host:ruta</c>) el host SÍ se extrae, y así debe ser: si git llegara
    /// a aceptar esa forma, lo que interesa es que la política evalúe ese destino. Devolver "no lo
    /// sé" también lo rechazaría, pero por el motivo equivocado — y un parser que se rinde ante una
    /// variante rara es exactamente por donde se cuela la siguiente.
    /// </summary>
    [Fact]
    public void An_empty_user_still_yields_the_host()
        => DiscoverTemplateHandler.ExtractHost("@host:ruta").Should().Be("host");
}
