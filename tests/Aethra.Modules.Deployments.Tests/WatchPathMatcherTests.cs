using Aethra.Modules.Deployments.Webhooks;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

/// <summary>
/// Tests del glob path-matcher que decide si un push afecta a un Template (filtra builds por
/// WatchPaths). Incluye la regresión del fix: <c>**/</c> matchea CERO o más segmentos, así que
/// <c>**/*.cs</c> también captura archivos .cs en la raíz (semántica gitignore).
/// </summary>
public sealed class WatchPathMatcherTests
{
    [Fact]
    public void Empty_watch_patterns_always_matches()
        => WatchPathMatcher.AnyMatches(["any/file.txt"], []).Should().BeTrue();

    [Fact]
    public void Empty_affected_paths_never_matches_when_patterns_exist()
        => WatchPathMatcher.AnyMatches([], ["backend/**"]).Should().BeFalse();

    [Theory]
    [InlineData("backend/**", "backend/app.cs", true)]
    [InlineData("backend/**", "backend/sub/deep.cs", true)]
    [InlineData("backend/**", "frontend/app.cs", false)]
    [InlineData("docs/*.md", "docs/readme.md", true)]
    [InlineData("docs/*.md", "docs/sub/readme.md", false)] // un solo * no cruza '/'
    [InlineData("docs/*.md", "docs/readme.txt", false)]
    [InlineData("Dockerfile", "Dockerfile", true)]
    [InlineData("Dockerfile", "src/Dockerfile", false)]
    [InlineData("src/app.?s", "src/app.cs", true)]    // ? = un char
    [InlineData("src/app.?s", "src/app.cves", false)] // ? = exactamente uno
    public void Matches_globs(string pattern, string path, bool expected)
        => WatchPathMatcher.AnyMatches([path], [pattern]).Should().Be(expected);

    [Theory]
    [InlineData("app.cs", true)]         // raíz (regresión del fix)
    [InlineData("src/app.cs", true)]
    [InlineData("src/sub/app.cs", true)]
    [InlineData("app.txt", false)]
    public void DoubleStarSlash_matches_zero_or_more_segments(string path, bool expected)
        => WatchPathMatcher.AnyMatches([path], ["**/*.cs"]).Should().Be(expected);

    [Fact]
    public void Matches_when_any_pattern_matches_any_path()
        => WatchPathMatcher.AnyMatches(["readme.md", "src/app.cs"], ["**/*.cs", "docs/*.md"]).Should().BeTrue();

    [Fact]
    public void No_match_when_nothing_overlaps()
        => WatchPathMatcher.AnyMatches(["readme.md"], ["**/*.cs"]).Should().BeFalse();
}
