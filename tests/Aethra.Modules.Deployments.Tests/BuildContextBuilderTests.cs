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
}
