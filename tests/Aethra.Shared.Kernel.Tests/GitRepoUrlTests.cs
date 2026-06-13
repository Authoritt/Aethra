using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Shared.Kernel.Tests;

/// <summary>
/// <see cref="GitRepoUrl"/> clasifica URLs de repo en HTTPS / SSH-scp / SSH-url y deriva el
/// nombre del repo. Es la entrada de todo template Git, así que la clasificación debe ser exacta.
/// </summary>
public sealed class GitRepoUrlTests
{
    [Theory]
    [InlineData("https://github.com/acme/app", GitRepoUrlKind.Https)]
    [InlineData("https://github.com/acme/app.git", GitRepoUrlKind.Https)]
    [InlineData("http://example.com/x/y", GitRepoUrlKind.Https)]
    [InlineData("git@github.com:acme/app.git", GitRepoUrlKind.SshScp)]
    [InlineData("ssh://git@github.com/acme/app.git", GitRepoUrlKind.SshUrl)]
    public void Create_classifies_valid_urls_by_kind(string input, GitRepoUrlKind expectedKind)
    {
        var result = GitRepoUrl.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(expectedKind);
        result.Value.Value.Should().Be(input);
    }

    [Fact]
    public void Create_trims_surrounding_whitespace()
    {
        var result = GitRepoUrl.Create("  https://github.com/acme/app  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("https://github.com/acme/app");
    }

    [Theory]
    [InlineData("", "git.empty")]
    [InlineData("   ", "git.empty")]
    [InlineData("ftp://host/repo", "git.format")]
    [InlineData("just-text", "git.format")]
    [InlineData("https://has space/x", "git.format")] // URLs con espacio interno se rechazan
    public void Create_rejects_invalid_urls_with_the_right_error_code(string input, string expectedCode)
    {
        var result = GitRepoUrl.Create(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("https://github.com/acme/app.git", "app")]
    [InlineData("https://github.com/acme/app", "app")]
    [InlineData("git@github.com:acme/app.git", "app")]
    [InlineData("ssh://git@github.com/acme/my-repo.git", "my-repo")]
    public void SuggestRepoName_returns_last_path_segment_without_git_suffix(string input, string expected)
    {
        var url = GitRepoUrl.Create(input).Value;

        url.SuggestRepoName().Should().Be(expected);
    }
}
