using Aethra.Api.Bootstrap;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// Pruebas del arranque de la API relativas a la bandera de backfill de URLs de monitores (regresión #32).
/// Demuestran que:
/// 1. Sin configurar la bandera, el backfill se ejecuta por defecto (default true) al leer la configuración efectiva.
/// 2. Cuando la bandera se configura explícitamente en false, el backfill no se ejecuta.
/// 3. El ciclo de arranque de la API respeta la bandera con un doble del paso.
/// </summary>
public sealed class MonitorUrlBackfillStartupTests
{
    [Fact]
    public void Configuracion_efectiva_sin_configurar_bandera_es_true_por_defecto()
    {
        var config = new ConfigurationBuilder().Build();

        MigrationsBootstrap.ShouldApplyMonitorUrlBackfill(config).Should().BeTrue();
        config.GetValue("Aethra:MonitorUrlBackfillOnStart", true).Should().BeTrue();
    }

    [Fact]
    public void Configuracion_efectiva_con_bandera_en_false_es_false()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aethra:MonitorUrlBackfillOnStart"] = "false",
            })
            .Build();

        MigrationsBootstrap.ShouldApplyMonitorUrlBackfill(config).Should().BeFalse();
        config.GetValue("Aethra:MonitorUrlBackfillOnStart", true).Should().BeFalse();
    }

    [Fact]
    public void Configuracion_efectiva_con_bandera_en_true_es_true()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aethra:MonitorUrlBackfillOnStart"] = "true",
            })
            .Build();

        MigrationsBootstrap.ShouldApplyMonitorUrlBackfill(config).Should().BeTrue();
        config.GetValue("Aethra:MonitorUrlBackfillOnStart", true).Should().BeTrue();
    }

    [Fact]
    public void Arranque_con_bandera_en_true_ejecuta_el_paso_de_backfill_con_doble()
    {
        var doubleStep = new TestBackfillStep();
        using var factory = new BackfillTestAppFactory(doubleStep, configFlagValue: "true");
        using var client = factory.CreateClient();

        doubleStep.Executed.Should().BeTrue("al estar habilitado el backfill, el arranque debe ejecutar el paso");
    }

    [Fact]
    public void Arranque_con_bandera_en_false_omite_el_paso_de_backfill_con_doble()
    {
        var doubleStep = new TestBackfillStep();
        using var factory = new BackfillTestAppFactory(doubleStep, configFlagValue: "false");
        using var client = factory.CreateClient();

        doubleStep.Executed.Should().BeFalse("con la bandera en false, el arranque no debe invocar el backfill");
    }

    private sealed class TestBackfillStep : IMonitorUrlBackfillStep
    {
        public bool Executed { get; private set; }

        public Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            Executed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class BackfillTestAppFactory(
        TestBackfillStep step,
        string configFlagValue) : AethraApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Aethra:MonitorUrlBackfillOnStart"] = configFlagValue,
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IMonitorUrlBackfillStep>(step);
            });
        }
    }
}
