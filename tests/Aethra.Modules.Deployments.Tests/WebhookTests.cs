using System.Security.Cryptography;
using System.Text;
using Aethra.Modules.Deployments.Webhooks;
using Aethra.Shared.Contracts.Projects;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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

    // ---------- WebhookTemplateAuthenticator ----------

    [Fact]
    public void FilterAuthenticatedTemplates_keeps_only_templates_verified_by_their_own_secret()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        var templates = new[]
        {
            Template("tpl-a", "secret-a"),
            Template("tpl-b", "secret-b"),
        };

        var authenticated = WebhookTemplateAuthenticator.FilterAuthenticatedTemplates(
            templates,
            Sign(body, "secret-b"),
            body);

        authenticated.Select(t => t.TemplateId).Should().Equal("tpl-b");
    }

    [Fact]
    public void FilterAuthenticatedTemplates_is_independent_of_template_order()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        var templates = new[]
        {
            Template("tpl-b", "secret-b"),
            Template("tpl-a", "secret-a"),
        };

        var authenticated = WebhookTemplateAuthenticator.FilterAuthenticatedTemplates(
            templates,
            Sign(body, "secret-a"),
            body);

        authenticated.Select(t => t.TemplateId).Should().Equal("tpl-a");
    }

    [Fact]
    public void FilterAuthenticatedTemplates_allows_multiple_templates_that_share_the_signature_secret()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        var templates = new[]
        {
            Template("tpl-a", "shared"),
            Template("tpl-b", "shared"),
            Template("tpl-c", "other"),
        };

        var authenticated = WebhookTemplateAuthenticator.FilterAuthenticatedTemplates(
            templates,
            Sign(body, "shared"),
            body);

        authenticated.Select(t => t.TemplateId).Should().Equal("tpl-a", "tpl-b");
    }

    [Fact]
    public void FilterAuthenticatedTemplates_skips_templates_without_a_configured_secret()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        var templates = new[]
        {
            Template("tpl-a", ""),
            Template("tpl-b", "   "),
            Template("tpl-c", "secret"),
        };

        var authenticated = WebhookTemplateAuthenticator.FilterAuthenticatedTemplates(
            templates,
            Sign(body, "secret"),
            body);

        authenticated.Select(t => t.TemplateId).Should().Equal("tpl-c");
    }

    [Fact]
    public void FilterAuthenticatedTemplates_returns_empty_when_no_template_secret_verifies()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        var templates = new[]
        {
            Template("tpl-a", "secret-a"),
            Template("tpl-b", "secret-b"),
        };

        var authenticated = WebhookTemplateAuthenticator.FilterAuthenticatedTemplates(
            templates,
            Sign(body, "wrong"),
            body);

        authenticated.Should().BeEmpty();
    }

    // ---------- GitHubWebhookBodyReader ----------

    [Fact]
    public async Task ReadAsync_rejects_declared_content_length_above_limit_without_reading_body()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 11;
        context.Request.Body = new ThrowOnReadStream();

        var result = await GitHubWebhookBodyReader.ReadAsync(context.Request, CancellationToken.None, maxBodyBytes: 10);

        result.IsPayloadTooLarge.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_accepts_declared_content_length_at_limit()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 10;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("0123456789"));

        var result = await GitHubWebhookBodyReader.ReadAsync(context.Request, CancellationToken.None, maxBodyBytes: 10);

        result.IsPayloadTooLarge.Should().BeFalse();
        Encoding.UTF8.GetString(result.Body).Should().Be("0123456789");
    }

    [Fact]
    public async Task ReadAsync_rejects_chunked_body_that_exceeds_limit()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = null;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("01234567890"));

        var result = await GitHubWebhookBodyReader.ReadAsync(context.Request, CancellationToken.None, maxBodyBytes: 10);

        result.IsPayloadTooLarge.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_accepts_chunked_body_at_limit()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = null;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("0123456789"));

        var result = await GitHubWebhookBodyReader.ReadAsync(context.Request, CancellationToken.None, maxBodyBytes: 10);

        result.IsPayloadTooLarge.Should().BeFalse();
        Encoding.UTF8.GetString(result.Body).Should().Be("0123456789");
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

    private static TemplateForBuildView Template(string templateId, string webhookSecret)
        => new(
            TemplateId: templateId,
            ProjectId: "project",
            Slug: templateId,
            Name: templateId,
            GitRepoUrl: "https://github.com/acme/repo.git",
            Branch: "main",
            WebhookSecret: webhookSecret,
            BaseDirectory: ".",
            WatchPaths: [],
            BuildType: "Dockerfile",
            DockerfilePath: "Dockerfile");

    private sealed class ThrowOnReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException("The stream should not be read.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The stream should not be read.");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
