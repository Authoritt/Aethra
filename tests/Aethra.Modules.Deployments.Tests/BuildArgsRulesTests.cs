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

    private static Dictionary<string, string> Env(params (string K, string V)[] kv) =>
        kv.ToDictionary(x => x.K, x => x.V);

    [Fact]
    public void Solo_env_de_instancia_build_time_produce_args()
    {
        var r = BuildArgsRules.Resolve(null, "s", "h.dev", Env(("NEXT_PUBLIC_URL", "https://h.dev")));
        r.Args.Should().BeEquivalentTo(new Dictionary<string, string> { ["NEXT_PUBLIC_URL"] = "https://h.dev" });
    }

    [Fact]
    public void Misma_clave_en_template_y_env_gana_la_env()
    {
        var r = BuildArgsRules.Resolve(
            [Kv("NEXT_PUBLIC_URL", "http://localhost:3001"), Kv("OTRA", "t")], "s", "h.dev",
            Env(("NEXT_PUBLIC_URL", "https://prod.dev")));
        r.Args["NEXT_PUBLIC_URL"].Should().Be("https://prod.dev");
        r.Args["OTRA"].Should().Be("t");
    }

    [Fact]
    public void Env_con_IsBuildTime_false_no_entra_porque_el_resolvedor_no_la_entrega()
    {
        // El filtro IsBuildTime vive en IEnvironmentResolver.ResolveBuildTimeEnvAsync: lo que no es
        // build-time no llega a la regla, asi que no aparece en los args.
        var r = BuildArgsRules.Resolve([Kv("A", "1")], "s", "h", Env());
        r.Args.Should().BeEquivalentTo(new Dictionary<string, string> { ["A"] = "1" });
    }

    [Fact]
    public void Env_interpola_domain_e_instance_y_falla_sin_host()
    {
        var ok = BuildArgsRules.Resolve(null, "slug", "x.dev", Env(("U", "https://{domain}/{instance}")));
        ok.Args["U"].Should().Be("https://x.dev/slug");

        var bad = BuildArgsRules.Resolve([Kv("U", "ok")], "slug", null, Env(("U", "https://{domain}")));
        bad.UnresolvedDomainKeys.Should().Equal("U");
        bad.Args.Should().NotContainKey("U");
    }

    // Guardas de CABLEADO: apps/api no tiene proyecto de tests; si el runner deja de usar la regla
    // o de pasar su resultado al BuildSpec, estos tests fallan.
    [Theory]
    [InlineData("BuildArgsRules.Resolve(template.BuildArgs, instance.Slug, hostname, buildTimeEnv)")]
    [InlineData("envResolver.ResolveBuildTimeEnvAsync(")]
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
