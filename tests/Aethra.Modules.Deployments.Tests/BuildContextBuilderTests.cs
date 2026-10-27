using Aethra.Modules.Deployments.Infrastructure.Build;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// Tests de los helpers de seguridad de tokens en <see cref="BuildContextBuilder"/>:
/// <c>ApplyAccessToken</c> inyecta el PAT en la URL HTTPS de clone de repos privados, sin tocar
/// SSH ni URLs con credenciales. Métodos internal expuestos al test vía InternalsVisibleTo.
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

    // ---------- Ref explícito: nunca construir otra rama (bug 2026-10-05) ----------

    [Theory]
    [InlineData("refs/heads/x", "x")]
    [InlineData("x", "x")]
    [InlineData("feature/y", "feature/y")]
    [InlineData("v1.0", "v1.0")]
    public void GitRefs_normalizes_only_the_heads_prefix(string input, string expected)
    {
        GitRefs.ToBranchShorthand(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("refs/heads/rama-x")]
    [InlineData("rama-x")]
    public async Task Builds_the_requested_branch_not_the_default(string requested)
    {
        using var repo = TempRepo.Create();
        var result = await Builder().BuildAsync(repo.Url, requested, null, "", null, CancellationToken.None);
        result.ResolvedSha.Should().Be(repo.BranchSha);
        result.ResolvedSha.Should().NotBe(repo.MainSha);
        TarContent(result.TarGz, "marker.txt").Should().Be("rama-x");
    }

    [Fact]
    public async Task A_missing_branch_fails_with_ref_not_found_instead_of_building_default()
    {
        using var repo = TempRepo.Create();
        var act = () => Builder().BuildAsync(repo.Url, "refs/heads/no-existe", null, "", null, CancellationToken.None);
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("ref_not_found");
    }

    [Fact]
    public async Task A_tag_ref_is_built()
    {
        using var repo = TempRepo.Create();
        var result = await Builder().BuildAsync(repo.Url, "v1.0", null, "", null, CancellationToken.None);
        result.ResolvedSha.Should().Be(repo.BranchSha);
        TarContent(result.TarGz, "marker.txt").Should().Be("rama-x");
    }

    private static BuildContextBuilder Builder() => new(NullLogger<BuildContextBuilder>.Instance);

    private static string TarContent(byte[] tarGz, string name)
    {
        using var gz = new GZipStream(new MemoryStream(tarGz), CompressionMode.Decompress);
        using var tar = new TarReader(gz);
        while (tar.GetNextEntry() is { } e)
        {
            if (e.Name == name)
            {
                using var r = new StreamReader(e.DataStream!);
                return r.ReadToEnd().Trim();
            }
        }
        throw new InvalidOperationException($"{name} no está en el tar");
    }

    /// <summary>Repo git local temporal: main (marker=main) y rama-x (marker=rama-x, tag v1.0).</summary>
    private sealed class TempRepo : IDisposable
    {
        private readonly string _dir;
        public string Url => new Uri(_dir).AbsoluteUri;
        public string MainSha { get; private set; } = "";
        public string BranchSha { get; private set; } = "";
        private TempRepo(string dir) => _dir = dir;

        public static TempRepo Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), "aethra-bcb-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var r = new TempRepo(dir);
            Git(dir, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(dir, "marker.txt"), "main");
            Git(dir, "add", "-A");
            Git(dir, "commit", "-q", "-m", "main");
            r.MainSha = Git(dir, "rev-parse", "HEAD").Trim();
            Git(dir, "checkout", "-q", "-b", "rama-x");
            File.WriteAllText(Path.Combine(dir, "marker.txt"), "rama-x");
            Git(dir, "commit", "-q", "-am", "rama");
            r.BranchSha = Git(dir, "rev-parse", "HEAD").Trim();
            Git(dir, "tag", "v1.0");
            Git(dir, "checkout", "-q", "main");
            return r;
        }

        private static string Git(string dir, params string[] args)
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false" })
            {
                psi.ArgumentList.Add(a);
            }
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', args)}: {p.StandardError.ReadToEnd()}");
            }
            return o;
        }

        public void Dispose()
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                }
                Directory.Delete(_dir, true);
            }
            catch (IOException) { }
        }
    }
}
