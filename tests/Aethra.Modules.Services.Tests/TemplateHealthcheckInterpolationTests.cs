using Aethra.Modules.Services.Templates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// OT-008 T15 #2: el healthcheck de la plantilla se interpola como env/command. Antes
/// <c>pg_isready -U ${admin_user}</c> viajaba literal y pg-iot-prod salia unhealthy estando sano.
/// Solo aplica a servicios creados desde ahora; los existentes conservan su healthcheck hasta recrearse.
/// </summary>
public sealed class TemplateHealthcheckInterpolationTests
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
    {
        ["admin_user"] = "aethra_admin",
        ["admin_password"] = "s3cr3t",
    };

    [Fact]
    public void Postgres_healthcheck_uses_the_real_admin_user()
    {
        var pg = new EmbeddedServiceTemplateCatalog(NullLogger<EmbeddedServiceTemplateCatalog>.Instance)
            .GetById("postgres-16")!;

        var hc = TemplateInterpolator.ApplyHealthcheck(pg.Healthcheck, Values)!;

        hc.Test.Should().Equal("CMD-SHELL", "pg_isready -U aethra_admin");
    }

    [Fact]
    public void No_template_healthcheck_keeps_an_unresolved_placeholder()
    {
        var all = new EmbeddedServiceTemplateCatalog(NullLogger<EmbeddedServiceTemplateCatalog>.Instance).GetAll();

        foreach (var t in all.Where(t => t.Healthcheck is not null))
        {
            var hc = TemplateInterpolator.ApplyHealthcheck(t.Healthcheck, Values)!;
            hc.Test.Should().NotContain(s => s.Contains("${", StringComparison.Ordinal), $"plantilla {t.Id}");
            hc.IntervalSeconds.Should().Be(t.Healthcheck!.IntervalSeconds);
            hc.Retries.Should().Be(t.Healthcheck.Retries);
        }
    }

    [Fact]
    public void Templates_without_placeholders_keep_their_healthcheck_unchanged()
    {
        var supabase = new EmbeddedServiceTemplateCatalog(NullLogger<EmbeddedServiceTemplateCatalog>.Instance)
            .GetById("supabase")!;
        TemplateInterpolator.ApplyHealthcheck(supabase.Healthcheck, Values)!.Test
            .Should().Equal(supabase.Healthcheck!.Test);
    }

    [Fact]
    public void Null_healthcheck_stays_null()
        => TemplateInterpolator.ApplyHealthcheck(null, Values).Should().BeNull();
}
