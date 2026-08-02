using Aethra.Modules.Monitoring.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aethra.Modules.Monitoring.Tests;

/// <summary>
/// Los dos hallazgos de la revision automatica del PR #26, comprobados antes de aceptarlos.
/// </summary>
public class MonitorWorkerTickRevisionTests
{
    /// <summary>
    /// Hallazgo 2: <c>Monitoring__TickSeconds=</c> (variable definida y VACIA, lo mas comun en un
    /// .env editado a medias) al ligarse a un <c>double</c> no anulable.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("diez")]
    public void Un_valor_vacio_o_ilegible_no_puede_tumbar_el_arranque(string crudo)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Monitoring:TickSeconds"] = crudo })
            .Build();
        var sp = new ServiceCollection()
            .Configure<MonitorWorkerOptions>(cfg.GetSection("Monitoring"))
            .BuildServiceProvider();

        // .Value materializa el binding: si el binder revienta, revienta AQUI -- y en produccion
        // reventaria dentro de ExecuteAsync, antes del primer await, tumbando el arranque entero
        // por una variable de entorno vacia.
        var acto = () => sp.GetRequiredService<IOptions<MonitorWorkerOptions>>().Value;

        acto.Should().NotThrow("una env vacia es la FALTA de una peticion, no una peticion invalida");
        TickResuelto.Desde(acto().TickSeconds).Efectivo
            .Should().Be(TimeSpan.FromSeconds(MonitorWorkerOptions.TickPorDefecto));
    }

    /// <summary>
    /// Hallazgo 1: el tick no es solo una cadencia de barrido, es la REJILLA sobre la que puede
    /// caer un sondeo. Un monitor solo se sondea en instantes multiplo del tick, asi que su
    /// cadencia real es el primer multiplo del tick que alcanza su intervalo — y como
    /// <c>LastCheckedAt</c> se graba al TERMINAR el probe (unos milisegundos despues del tick), el
    /// multiplo exacto se queda corto por epsilon y el sondeo se salta hasta el siguiente.
    /// </summary>
    [Theory]
    [InlineData(10, 30, 30)]   // default: un monitor de 30s debe salir cada 30s, no cada 40
    [InlineData(30, 30, 30)]   // techo: no puede degenerar a 60
    [InlineData(5, 30, 30)]
    [InlineData(10, 60, 60)]
    // Rejilla que no divide al intervalo: la holgura resuelve hacia ANTES (28s), no hacia despues
    // (35s). Es el lado correcto en el que fallar — adelantarse menos de medio tick mantiene la
    // cadencia media en el intervalo pedido o por debajo; retrasarse la infla en silencio, que es
    // justo el defecto que esta prueba existe para impedir.
    [InlineData(7, 30, 28)]
    public void La_cadencia_real_no_puede_ser_el_doble_de_la_anunciada(
        double tickSeg, int intervaloSeg, double cadenciaEsperada)
    {
        const double epsilon = 0.05;   // lo que tarda el probe entre el tick y grabar LastCheckedAt
        var tolerancia = MonitorWorkerOptions.ToleranciaDeRejilla(TimeSpan.FromSeconds(tickSeg));

        // Primer multiplo del tick en el que el filtro de "toca" da verdadero.
        double cadencia = 0;
        for (var k = 1; k <= 1000; k++)
        {
            var instante = k * tickSeg;
            if (instante - epsilon >= intervaloSeg - tolerancia.TotalSeconds)
            {
                cadencia = instante;
                break;
            }
        }

        cadencia.Should().Be(cadenciaEsperada,
            "un monitor de {0}s con tick {1}s debe sondearse cada {2}s", intervaloSeg, tickSeg, cadenciaEsperada);
    }
}
