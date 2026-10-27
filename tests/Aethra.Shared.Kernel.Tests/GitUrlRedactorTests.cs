using Aethra.Shared.Kernel.Security;
using FluentAssertions;
using Xunit;

namespace Aethra.Shared.Kernel.Tests;

public sealed class GitUrlRedactorTests
{
    [Theory]
    [InlineData("https://user:password@github.com/acme/app.git", "https://***@github.com/acme/app.git")]
    [InlineData("http://personal-access-token@git.example.com/acme/app", "http://***@git.example.com/acme/app")]
    [InlineData("ssh://user:password@git.example.com/acme/app.git", "ssh://***@git.example.com/acme/app.git")]
    public void Redact_removes_credentials_from_git_urls(string input, string expected)
    {
        GitUrlRedactor.Redact(input).Should().Be(expected);
    }

    [Fact]
    public void Redact_removes_a_credentialed_url_embedded_in_git_stderr()
    {
        const string stderr = "fatal: repository 'https://build-user:secret@git.example.com/acme/app.git/' not found";

        var redacted = GitUrlRedactor.Redact(stderr);

        redacted.Should().Be("fatal: repository 'https://***@git.example.com/acme/app.git/' not found");
        redacted.Should().NotContain("build-user");
        redacted.Should().NotContain("secret");
    }

    [Fact]
    public void Redact_removes_the_raw_and_url_encoded_sensitive_value()
    {
        var redacted = GitUrlRedactor.Redact(
            "token=a/b; clone https://x-access-token:a%2Fb@host/repo failed", "a/b");

        redacted.Should().NotContain("a/b");
        redacted.Should().NotContain("a%2Fb");
        redacted.Should().Be("token=***; clone https://***@host/repo failed");
    }

    [Theory]
    [InlineData("https://github.com/acme/app.git")]
    [InlineData("git@github.com:acme/app.git")]
    [InlineData("ssh://git@github.com/acme/app.git")]
    public void Redact_leaves_urls_without_secrets_unchanged(string input)
    {
        GitUrlRedactor.Redact(input).Should().Be(input);
    }
}
