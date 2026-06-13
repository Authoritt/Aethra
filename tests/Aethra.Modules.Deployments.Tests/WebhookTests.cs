using System.Security.Cryptography;
using System.Text;
using Aethra.Modules.Deployments.Webhooks;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// Tests del borde de entrada del push-to-deploy: la verificación HMAC del webhook de GitHub
/// (<see cref="GitHubSignatureValidator"/>) y el parsing del payload (<see cref="GitHubPushPayload"/>).
/// Crítico para seguridad y para que un push a un TAG no dispare builds de branch.
/// </summary>
public sealed class WebhookTests
{
    private static string Sign(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(body));
    }

    // ---------- GitHubSignatureValidator ----------

    [Fact]
    public void Validate_accepts_a_known_hmac_vector()
    {
        // Vector estándar HMAC-SHA256(key="key", "The quick brown fox jumps over the lazy dog").
        var body = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        const string header = "sha256=f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8";

        GitHubSignatureValidator.Validate(header, body, "key").Should().BeTrue();
    }

    [Fact]
    public void Validate_accepts_uppercase_presented_hex()
    {
        var body = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        const string header = "sha256=F7BC83F430538424B13298E6AA6FB143EF4D59A14946175997479DBC2D1A3CD8";

        GitHubSignatureValidator.Validate(header, body, "key").Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_wrong_secret()
    {
        var body = Encoding.UTF8.GetBytes("payload");

        GitHubSignatureValidator.Validate(Sign(body, "secret"), body, "wrong-secret").Should().BeFalse();
    }

    [Fact]
    public void Validate_rejects_a_tampered_body()
    {
        var signed = Sign(Encoding.UTF8.GetBytes("original"), "s");

        GitHubSignatureValidator.Validate(signed, Encoding.UTF8.GetBytes("tampered"), "s").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deadbeef")]      // sin prefijo
    [InlineData("md5=deadbeef")]  // prefijo equivocado
    public void Validate_rejects_missing_or_bad_signature_header(string? header)
    {
        GitHubSignatureValidator.Validate(header, Encoding.UTF8.GetBytes("x"), "s").Should().BeFalse();
    }

    [Fact]
    public void Validate_rejects_empty_secret()
    {
        var body = Encoding.UTF8.GetBytes("x");

        GitHubSignatureValidator.Validate(Sign(body, "s"), body, "").Should().BeFalse();
    }

    // ---------- GitHubPushPayload ----------

    [Theory]
    [InlineData("refs/heads/main", "main")]
    [InlineData("refs/heads/feature/x", "feature/x")]
    [InlineData("refs/tags/v1.0", null)] // un tag NO es un branch → no build de branch
    [InlineData("garbage", null)]
    [InlineData(null, null)]
    public void Branch_parses_only_heads_refs(string? gitRef, string? expected)
    {
        new GitHubPushPayload { Ref = gitRef }.Branch.Should().Be(expected);
    }

    [Fact]
    public void HeadSha_prefers_after_then_head_commit_then_null()
    {
        new GitHubPushPayload { After = "sha-after", HeadCommit = new GitHubCommit { Id = "sha-head" } }
            .HeadSha.Should().Be("sha-after");
        new GitHubPushPayload { After = null, HeadCommit = new GitHubCommit { Id = "sha-head" } }
            .HeadSha.Should().Be("sha-head");
        new GitHubPushPayload().HeadSha.Should().BeNull();
    }

    [Fact]
    public void AllAffectedPaths_dedups_added_modified_removed_across_commits()
    {
        var payload = new GitHubPushPayload
        {
            Commits =
            [
                new GitHubCommit { Added = ["a.txt"], Modified = ["b.txt"] },
                new GitHubCommit { Modified = ["b.txt"], Removed = ["c.txt"] },
            ],
        };

        payload.AllAffectedPaths().Should().BeEquivalentTo(["a.txt", "b.txt", "c.txt"]);
    }

    [Fact]
    public void CandidateRepoUrls_yields_non_blank_urls_in_order()
    {
        var payload = new GitHubPushPayload
        {
            Repository = new GitHubRepository { CloneUrl = "https://c", SshUrl = "   ", HtmlUrl = "https://h" },
        };

        payload.CandidateRepoUrls().Should().Equal("https://c", "https://h");
    }

    [Fact]
    public void CandidateRepoUrls_is_empty_without_a_repository()
    {
        new GitHubPushPayload().CandidateRepoUrls().Should().BeEmpty();
    }
}
