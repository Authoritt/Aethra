using Aethra.Modules.Services.Templates;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// <see cref="TemplateInterpolator"/> sustituye placeholders <c>${name}</c> (dollar-brace) en env/
/// command de un servicio con los valores del binding (admin_user/admin_password). Distinto del
/// token <c>{instance}</c> del native-deploy (sin '$'), que NO debe matchear acá.
/// </summary>
public sealed class TemplateInterpolatorTests
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
    {
        ["admin_user"] = "aethra",
        ["admin_password"] = "s3cr3t",
    };

    [Fact]
    public void Apply_string_replaces_known_placeholders()
        => TemplateInterpolator.Apply("user=${admin_user};pass=${admin_password}", Values)
            .Should().Be("user=aethra;pass=s3cr3t");

    [Fact]
    public void Apply_string_leaves_unknown_placeholders_intact()
        => TemplateInterpolator.Apply("x=${unknown}", Values).Should().Be("x=${unknown}");

    [Fact]
    public void Apply_string_returns_empty_and_plain_unchanged()
    {
        TemplateInterpolator.Apply("", Values).Should().BeEmpty();
        TemplateInterpolator.Apply("plain text", Values).Should().Be("plain text");
    }

    [Fact]
    public void Apply_replaces_repeated_placeholders()
        => TemplateInterpolator.Apply("${admin_user}-${admin_user}", Values).Should().Be("aethra-aethra");

    [Fact]
    public void Apply_does_not_match_bare_braces_or_lone_dollar()
        => TemplateInterpolator.Apply("{admin_user} $admin_user", Values)
            .Should().Be("{admin_user} $admin_user");

    [Fact]
    public void Apply_dictionary_interpolates_values_but_not_keys()
    {
        var input = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DB_USER"] = "${admin_user}",
            ["${admin_user}"] = "literal-key", // la clave con forma de placeholder NO se toca
        };

        var result = TemplateInterpolator.Apply(input, Values);

        result["DB_USER"].Should().Be("aethra");
        result.Should().ContainKey("${admin_user}");
    }

    [Fact]
    public void Apply_list_interpolates_each_element()
    {
        var input = new[] { "${admin_user}", "static" };

        TemplateInterpolator.Apply(input, Values).Should().Equal("aethra", "static");
    }

    [Fact]
    public void Apply_list_preserves_null()
        => TemplateInterpolator.Apply((IReadOnlyList<string>?)null, Values).Should().BeNull();
}
