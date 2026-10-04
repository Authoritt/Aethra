using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

public sealed class BuildArgsRulesTests
{
    private static KeyValuePair<string, string> Kv(string k, string v) => new(k, v);

    [Fact]
    public void Sin_BuildArgs_el_resultado_es_vacio_como_hoy()
    {
        BuildArgsRules.Resolve(null, "app", "app.x.dev").Args.Should().BeEmpty();
        BuildArgsRules.Resolve([], "app", null).Args.Should().BeEmpty();
        BuildArgsRules.Resolve([], "app", null).UnresolvedDomainKeys.Should().BeEmpty();
    }

    [Fact]
    public void Interpola_instance_y_domain()
    {
        var r = BuildArgsRules.Resolve(
            [Kv("NEXT_PUBLIC_API_URL", "https://api.{domain}"), Kv("SLUG", "{instance}-x"), Kv("FIJO", "1")],
            "inonetickets-prod", "inonetickets.lat");

        r.Args.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["NEXT_PUBLIC_API_URL"] = "https://api.inonetickets.lat",
            ["SLUG"] = "inonetickets-prod-x",
            ["FIJO"] = "1",
        });
        r.UnresolvedDomainKeys.Should().BeEmpty();
    }

    [Fact]
    public void Domain_sin_host_se_reporta_y_no_se_hornea_el_token()
    {
        var r = BuildArgsRules.Resolve([Kv("URL", "https://{domain}"), Kv("OK", "a")], "s", "  ");
        r.UnresolvedDomainKeys.Should().Equal("URL");
        r.Args.Should().ContainKey("OK").And.NotContainKey("URL");
    }

    // Guardas de CABLEADO: apps/api no tiene proyecto de tests; si el runner deja de usar la regla
    // o de pasar su resultado al BuildSpec, estos tests fallan.
    [Theory]
    [InlineData("BuildArgsRules.Resolve(template.BuildArgs, instance.Slug, hostname)")]
    [InlineData("BuildArgs: buildArgs.Args,")]
    public void El_runner_pasa_los_BuildArgs_del_template_al_build_git(string expected)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aethra.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull();
        File.ReadAllText(Path.Combine(dir!.FullName, "apps/api/Bootstrap/NativeDeployRunner.cs"))
            .Replace("\r\n", "\n").Should().Contain(expected);
    }
}
