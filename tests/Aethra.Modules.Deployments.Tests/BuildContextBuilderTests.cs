using Aethra.Modules.Deployments.Infrastructure.Build;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// Tests de los helpers de seguridad de tokens en <see cref="BuildContextBuilder"/>:
/// <c>ApplyAccessToken</c> (inyecta el PAT en la URL HTTPS de clone de repos privados, sin tocar
/// SSH ni URLs con credenciales) y <c>Redact</c> (la red de seguridad que mantiene el token fuera
/// de los logs / mensajes de error). Métodos internal expuestos al test vía InternalsVisibleTo.
/// </summary>
public sealed class BuildContextBuilderTests
{
    // ---------- ApplyAccessToken ----------

    [Fact]
    public void ApplyAccessToken_injects_token_into_https_url()
    {
        BuildContextBuilder.ApplyAccessToken("https://github.com/acme/app.git", "ghp_token123")
            .Should().Be("https://x-access-token:ghp_token123@github.com/acme/app.git");
    }

    [Theory]
    [InlineData("git@github.com:acme/app.git")]        // SSH scp
    [InlineData("ssh://git@github.com/acme/app.git")]  // SSH url
    public void ApplyAccessToken_leaves_non_https_urls_unchanged(string url)
    {
        BuildContextBuilder.ApplyAccessToken(url, "ghp_token").Should().Be(url);
    }

    [Fact]
    public void ApplyAccessToken_leaves_already_credentialed_url_unchanged()
    {
        const string url = "https://user:pass@github.com/acme/app.git";

        BuildContextBuilder.ApplyAccessToken(url, "ghp_token").Should().Be(url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyAccessToken_without_a_token_leaves_url_unchanged(string? token)
    {
        const string url = "https://github.com/acme/app.git";

        BuildContextBuilder.ApplyAccessToken(url, token).Should().Be(url);
    }

    [Fact]
    public void ApplyAccessToken_url_encodes_a_token_with_special_chars()
    {
        BuildContextBuilder.ApplyAccessToken("https://github.com/o/r", "a/b+c")
            .Should().Contain("x-access-token:a%2Fb%2Bc@");
    }

    // ---------- Redact ----------

    [Fact]
    public void Redact_removes_the_raw_token_from_text()
    {
        var redacted = BuildContextBuilder.Redact(
            "fatal: clone https://x-access-token:ghp_secret@github.com/o/r failed", "ghp_secret");

        redacted.Should().NotContain("ghp_secret");
        redacted.Should().Contain("***");
    }

    [Fact]
    public void Redact_removes_the_url_encoded_token_from_text()
    {
        // El token "a/b" viaja url-encoded como "a%2Fb" en la clone URL.
        var redacted = BuildContextBuilder.Redact("clone https://x-access-token:a%2Fb@host failed", "a/b");

        redacted.Should().NotContain("a%2Fb");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_without_a_token_returns_text_unchanged(string? token)
    {
        BuildContextBuilder.Redact("nothing to redact here", token).Should().Be("nothing to redact here");
    }

    // ---------- ResolvedShaSatisfiesRequest ----------

    /// <summary>
    /// Sin commit pedido se construye lo que haya en el branch: es el contrato normal de un deploy
    /// por rama y no hay nada que verificar.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvedSha_without_a_requested_commit_is_always_satisfied(string? requested)
    {
        BuildContextBuilder.ResolvedShaSatisfiesRequest(requested, "a9f88cbdeadbeef").Should().BeTrue();
    }

    [Fact]
    public void ResolvedSha_equal_to_the_requested_one_satisfies_it()
    {
        BuildContextBuilder.ResolvedShaSatisfiesRequest("a9f88cb", "a9f88cb").Should().BeTrue();
    }

    /// <summary>El mismo commit escrito en otra caja sigue siendo el mismo commit.</summary>
    [Fact]
    public void ResolvedSha_comparison_ignores_case()
    {
        BuildContextBuilder.ResolvedShaSatisfiesRequest("A9F88CB", "a9f88cb").Should().BeTrue();
    }

    /// <summary>
    /// El caso que motiva la comprobación: se pidió un commit y el árbol quedó en otro. Antes esto
    /// se anotaba en el log y se empaquetaba igual, produciendo un artefacto que declaraba un commit
    /// que no contenía.
    /// </summary>
    [Fact]
    public void ResolvedSha_different_from_the_requested_one_is_rejected()
    {
        BuildContextBuilder.ResolvedShaSatisfiesRequest("a9f88cb", "d7e38aa").Should().BeFalse();
    }

    /// <summary>
    /// Un SHA ABREVIADO satisface al commit completo que git resolvió. <c>GitSha.Create</c> acepta
    /// hex de 7 a 40 caracteres, pero <c>git rev-parse HEAD</c> devuelve siempre 40: exigir igualdad
    /// exacta rechazaría toda build lanzada con un SHA corto, que es un caso soportado.
    ///
    /// <para>Aceptar el prefijo no reabre la ambigüedad: si el nombre abreviado fuera ambiguo,
    /// <c>git checkout</c> habría fallado antes de llegar aquí. La desambiguación la hace git contra
    /// el repo real; esta comprobación solo descarta que el árbol acabara en OTRO commit.</para>
    /// </summary>
    [Theory]
    [InlineData("a9f88cb", "a9f88cbdeadbeef1234567890abcdef123456789")]
    [InlineData("a9f88cbdead", "a9f88cbdeadbeef1234567890abcdef123456789")]
    public void An_abbreviated_sha_is_satisfied_by_the_full_commit_git_resolved(string requested, string resolved)
    {
        BuildContextBuilder.ResolvedShaSatisfiesRequest(requested, resolved).Should().BeTrue();
    }

    /// <summary>
    /// Lo contrario sí es un fallo: si lo pedido es MÁS largo que lo resuelto, el árbol no puede ser
    /// el commit solicitado.
    /// </summary>
    [Fact]
    public void A_requested_sha_longer_than_the_resolved_one_is_rejected()
    {
        BuildContextBuilder.ResolvedShaSatisfiesRequest("a9f88cbdeadbeef", "a9f88cb").Should().BeFalse();
    }

    /// <summary>Si no se pudo resolver HEAD, no se puede afirmar que sea el commit pedido.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unresolvable_head_never_satisfies_a_requested_commit(string? resolved)
    {
        BuildContextBuilder.ResolvedShaSatisfiesRequest("a9f88cb", resolved).Should().BeFalse();
    }
}
