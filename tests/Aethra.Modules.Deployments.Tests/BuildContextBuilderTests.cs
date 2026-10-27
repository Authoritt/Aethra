using Aethra.Modules.Deployments.Infrastructure.Build;
using System.Text;
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
#pragma warning disable CS0618 // Type or member is obsolete

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
#pragma warning restore CS0618

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

    // ---------- Issue #81: Git access token kept out of argv and config ----------

    [Fact]
    public void BuildGitAuthEnvironment_injects_scoped_authorization_header_for_https()
    {
        var env = BuildContextBuilder.BuildGitAuthEnvironment("https://github.com/acme/private-repo.git", "ghp_secret_123");

        env.Should().NotBeNull();
        env!["GIT_CONFIG_COUNT"].Should().Be("1");
        env["GIT_CONFIG_KEY_0"].Should().Be("http.https://github.com/.extraHeader");
        env["GIT_CONFIG_VALUE_0"].Should().StartWith("Authorization: Basic ");

        var b64 = env["GIT_CONFIG_VALUE_0"]["Authorization: Basic ".Length..];
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        decoded.Should().Be("x-access-token:ghp_secret_123");
    }

    [Fact]
    public void BuildGitAuthEnvironment_preserves_custom_port_in_authority_prefix()
    {
        var env = BuildContextBuilder.BuildGitAuthEnvironment("https://gitlab.example.com:8443/acme/app.git", "token456");

        env.Should().NotBeNull();
        env!["GIT_CONFIG_KEY_0"].Should().Be("http.https://gitlab.example.com:8443/.extraHeader");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildGitAuthEnvironment_returns_null_when_token_is_missing(string? token)
    {
        BuildContextBuilder.BuildGitAuthEnvironment("https://github.com/acme/app.git", token)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("git@github.com:acme/app.git")]
    [InlineData("ssh://git@github.com/acme/app.git")]
    [InlineData("file:///tmp/repo.git")]
    [InlineData("http://github.com/acme/app.git")]
    public void BuildGitAuthEnvironment_returns_null_for_non_https_urls(string url)
    {
        BuildContextBuilder.BuildGitAuthEnvironment(url, "token123")
            .Should().BeNull();
    }

    [Fact]
    public void BuildGitAuthEnvironment_appends_to_inherited_git_config_count()
    {
        var existingEnv = new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "2",
            ["GIT_CONFIG_KEY_0"] = "http.sslVerify",
            ["GIT_CONFIG_VALUE_0"] = "false",
            ["GIT_CONFIG_KEY_1"] = "user.name",
            ["GIT_CONFIG_VALUE_1"] = "ci-runner"
        };

        var env = BuildContextBuilder.BuildGitAuthEnvironment(
            "https://github.com/acme/private-repo.git",
            "ghp_token_abc",
            existingEnv);

        env.Should().NotBeNull();
        env!["GIT_CONFIG_COUNT"].Should().Be("3");
        env["GIT_CONFIG_KEY_2"].Should().Be("http.https://github.com/.extraHeader");
        env["GIT_CONFIG_VALUE_2"].Should().StartWith("Authorization: Basic ");
        env.ContainsKey("GIT_CONFIG_KEY_0").Should().BeFalse();
        env.ContainsKey("GIT_CONFIG_KEY_1").Should().BeFalse();
    }

    [Fact]
    public void BuildGitAuthEnvironment_returns_null_when_url_already_has_credentials()
    {
        BuildContextBuilder.BuildGitAuthEnvironment("https://user:pass@github.com/acme/app.git", "token123")
            .Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_with_access_token_does_not_expose_token_in_process_arguments()
    {
        const string secretToken = "ghp_super_secret_pat_987654321";
        const string repoUrl = "https://github.com/acme/private-repo.git";
        const string testSha = "a9f88cb1234567890abcdef1234567890abcdef12";

        var capturedStartInfos = new List<ProcessStartInfo>();
        var builder = new BuildContextBuilder(
            NullLogger<BuildContextBuilder>.Instance,
            (psi, _) =>
            {
                capturedStartInfos.Add(psi);
                var cmd = psi.ArgumentList.Count > 0 ? psi.ArgumentList[0] : "";
                return cmd switch
                {
                    "clone" => Task.FromResult((0, "", "")),
                    "rev-parse" => Task.FromResult((0, $"{testSha}\n", "")),
                    _ => Task.FromResult((0, "", ""))
                };
            });

        var result = await builder.BuildAsync(
            repoUrl,
            branch: "main",
            gitSha: testSha,
            baseDirectory: "",
            accessToken: secretToken,
            CancellationToken.None);

        result.ResolvedSha.Should().Be(testSha);
        capturedStartInfos.Should().NotBeEmpty();

        // Verificamos que NINGÚN argumento de NINGÚN proceso git hijo contenga el token
        foreach (var psi in capturedStartInfos)
        {
            foreach (var arg in psi.ArgumentList)
            {
                arg.Should().NotContain(secretToken);
                arg.Should().NotContain(Uri.EscapeDataString(secretToken));
                arg.Should().NotContain("x-access-token");
            }
            psi.Arguments.Should().NotContain(secretToken);
        }

        // Inspeccionamos la invocación del clone
        var clonePsi = capturedStartInfos.First(p => p.ArgumentList.Contains("clone"));
        clonePsi.ArgumentList.Should().Contain(repoUrl);
        clonePsi.Environment["GIT_CONFIG_COUNT"].Should().Be("1");
        clonePsi.Environment["GIT_CONFIG_KEY_0"].Should().Be("http.https://github.com/.extraHeader");
        clonePsi.Environment["GIT_CONFIG_VALUE_0"].Should().Contain(
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{secretToken}")));

        // El log no debe contener el token
        foreach (var logLine in result.Log)
        {
            logLine.Should().NotContain(secretToken);
        }
    }

    [Fact]
    public async Task BuildAsync_with_access_token_passes_auth_to_fetch_without_argv_exposure()
    {
        const string secretToken = "ghp_fetch_secret_token_abc";
        const string repoUrl = "https://github.com/acme/private-repo.git";
        const string cloneHeadSha = "1111111111111111111111111111111111111111";
        const string requestedSha = "2222222222222222222222222222222222222222";

        var capturedStartInfos = new List<ProcessStartInfo>();
        var revParseCount = 0;
        var builder = new BuildContextBuilder(
            NullLogger<BuildContextBuilder>.Instance,
            (psi, _) =>
            {
                capturedStartInfos.Add(psi);
                var cmd = psi.ArgumentList.Count > 0 ? psi.ArgumentList[0] : "";
                return cmd switch
                {
                    "clone" => Task.FromResult((0, "", "")),
                    "rev-parse" => Task.FromResult((0, $"{((++revParseCount == 1) ? cloneHeadSha : requestedSha)}\n", "")),
                    "fetch" => Task.FromResult((0, "", "")),
                    "checkout" => Task.FromResult((0, "", "")),
                    _ => Task.FromResult((0, "", ""))
                };
            });

        var result = await builder.BuildAsync(
            repoUrl,
            branch: "main",
            gitSha: requestedSha,
            baseDirectory: "",
            accessToken: secretToken,
            CancellationToken.None);

        result.ResolvedSha.Should().Be(requestedSha);

        var fetchPsi = capturedStartInfos.FirstOrDefault(p => p.ArgumentList.Contains("fetch"));
        fetchPsi.Should().NotBeNull();
        foreach (var arg in fetchPsi!.ArgumentList)
        {
            arg.Should().NotContain(secretToken);
        }
        fetchPsi.Environment["GIT_CONFIG_COUNT"].Should().Be("1");
        fetchPsi.Environment["GIT_CONFIG_KEY_0"].Should().Be("http.https://github.com/.extraHeader");
    }

    [Fact]
    public async Task BuildAsync_with_access_token_on_clone_failure_redacts_token_from_exception()
    {
        const string secretToken = "secret_error_token_999";
        var builder = new BuildContextBuilder(
            NullLogger<BuildContextBuilder>.Instance,
            (_, _) => Task.FromResult((128, "", $"fatal: authentication failed for token {secretToken}")));

        var act = () => builder.BuildAsync(
            "https://github.com/acme/private-repo.git",
            branch: "main",
            gitSha: null,
            baseDirectory: "",
            accessToken: secretToken,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().NotContain(secretToken);
        ex.Which.Message.Should().Contain("***");
    }

    [Fact]
    public async Task BuildAsync_with_local_file_repo_ignores_token_and_does_not_set_git_config_auth()
    {
        const string dummyToken = "token_local_test_555";
        using var repo = TempRepo.Create();

        var capturedStartInfos = new List<ProcessStartInfo>();
        var builder = new BuildContextBuilder(
            NullLogger<BuildContextBuilder>.Instance,
            async (psi, ct) =>
            {
                capturedStartInfos.Add(psi);
                return await BuildContextBuilder.DefaultGitRunner(psi, ct);
            });

        var result = await builder.BuildAsync(repo.Url, "main", null, "", dummyToken, CancellationToken.None);

        result.ResolvedSha.Should().Be(repo.MainSha);
        capturedStartInfos.Should().NotBeEmpty();

        foreach (var psi in capturedStartInfos)
        {
            foreach (var arg in psi.ArgumentList)
            {
                arg.Should().NotContain(dummyToken);
            }
            psi.Arguments.Should().NotContain(dummyToken);
            // Repos locales (file://): BuildGitAuthEnvironment devuelve null, no se inyecta GIT_CONFIG_*
            psi.Environment.ContainsKey("GIT_CONFIG_KEY_0").Should().BeFalse();
            psi.Environment.ContainsKey("GIT_CONFIG_VALUE_0").Should().BeFalse();
        }
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
