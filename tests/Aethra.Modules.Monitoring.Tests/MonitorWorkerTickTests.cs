using Aethra.Modules.Monitoring.Domain;
using Aethra.Modules.Monitoring.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Monitoring.Tests;

/// <summary>
/// El tick del worker, que hasta el issue #25 era una constante privada mientras los dos README
/// prometian que se podia configurar.
///
/// <para>
/// Lo que estos tests protegen no es el numero, es la <b>relacion</b>: el techo del tick es el
/// intervalo minimo que un monitor puede pedir, porque por encima de el ese monitor no puede
/// sondearse a su ritmo por construccion. Escrito como constante suelta, un futuro cambio del
/// dominio dejaria el techo apuntando a nada.
/// </para>
/// </summary>
public class MonitorWorkerTickTests
{
    [Fact]
    public void El_techo_del_tick_es_el_intervalo_minimo_de_un_monitor()
    {
        // Si esto se rompe, o el dominio cambio o alguien clavo un numero: las dos cosas se miran.
        MonitorWorkerOptions.TickMaximo.Should().Be(Aethra.Modules.Monitoring.Domain.Monitor.MinIntervalSec);
    }

    [Fact]
    public void Sin_configurar_nada_el_tick_es_el_de_siempre()
    {
        // La opcion se anade para poder tocarla, no para cambiar lo que ya corria en produccion.
        new MonitorWorkerOptions().TickSeconds.Should().BeNull("sin configurar es AUSENTE, no 10");
        TickResuelto.Desde(new MonitorWorkerOptions().TickSeconds)
            .Efectivo.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(29.5)]
    [InlineData(30)]
    public void Un_valor_dentro_del_rango_se_respeta_tal_cual(double segundos)
    {
        var t = TickResuelto.Desde(segundos);
        t.Efectivo.Should().Be(TimeSpan.FromSeconds(segundos));
        t.Recortado.Should().BeFalse("nada que avisar cuando el operador pide algo alcanzable");
    }

    [Theory]
    [InlineData(0.5, 1)]      // por debajo del suelo: bucle ocupado sin granularidad util
    [InlineData(0.001, 1)]
    [InlineData(60, 30)]      // por encima del techo: un monitor de 30s ya no llega a tiempo
    [InlineData(3600, 30)]
    public void Un_valor_fuera_del_rango_se_recorta_Y_SE_DICE(double pedido, double esperado)
    {
        var t = TickResuelto.Desde(pedido);
        t.Efectivo.Should().Be(TimeSpan.FromSeconds(esperado));
        t.Pedido.Should().Be(pedido, "hay que poder decirle al operador que pidio, no solo que se uso");
        t.Recortado.Should().BeTrue(
            "un recorte callado deja al operador creyendo que configuro algo que no esta pasando");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Ausente_o_basura_es_la_FALTA_de_una_peticion_y_no_se_avisa(double segundos)
    {
        // Distinto de un valor fuera de rango: aqui nadie pidio nada (json sin la clave, env vacia).
        // Avisar de esto entrenaria al operador a ignorar el aviso que si importa.
        var t = TickResuelto.Desde(segundos);
        t.Efectivo.Should().Be(TimeSpan.FromSeconds(MonitorWorkerOptions.TickPorDefecto));
        t.Recortado.Should().BeFalse();
    }
}
