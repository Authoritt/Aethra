using Aethra.Modules.Deployments.Rollout;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Deployments.Tests;

public sealed class EnvPrecedenceRulesTests
{
    [Fact]
    public void Merge_devuelve_el_mismo_diccionario_que_el_algoritmo_actual()
    {
        var baseEnv = new Dictionary<string, string>
        {
            ["DATABASE_URL"] = "postgres://base",
            ["SHARED"] = "base",
        };
        KeyValuePair<string, string>[] svcEnv =
        [
            new("SHARED", "svc-{instance}"),
            new("ONLY_SERVICE", "value-{instance}"),
        ];

        var merged = EnvPrecedenceRules.Merge(baseEnv, svcEnv, "iot-prod");

        merged.Env.Should().Equal(new Dictionary<string, string>
        {
            ["DATABASE_URL"] = "postgres://base",
            ["SHARED"] = "svc-iot-prod",
            ["ONLY_SERVICE"] = "value-iot-prod",
        });
    }

    [Fact]
    public void Reporta_las_claves_donde_el_servicio_pisa_un_valor_distinto()
    {
        var merged = EnvPrecedenceRules.Merge(
            new Dictionary<string, string> { ["PUBLIC_URL"] = "https://base.example.com" },
            [new("PUBLIC_URL", "https://svc.example.com")],
            "yunke");

        merged.OverriddenKeys.Should().Equal("PUBLIC_URL");
    }

    [Fact]
    public void No_reporta_warning_si_el_valor_resultante_es_el_mismo()
    {
        var merged = EnvPrecedenceRules.Merge(
            new Dictionary<string, string> { ["PUBLIC_URL"] = "https://yunke.example.com" },
            [new("PUBLIC_URL", "https://{instance}.example.com")],
            "yunke");

        merged.OverriddenKeys.Should().BeEmpty();
    }
}
