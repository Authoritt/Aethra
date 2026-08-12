using Aethra.Modules.Projects.Domain.Instances;
using Aethra.Shared.Kernel.Primitives;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Projects.Tests;

/// <summary>
/// Validación de la configuración de puertos y healthcheck de una instancia.
///
/// <para>Los tres defectos que cubren estos tests comparten forma: una entrada inválida no se
/// rechazaba, se <b>convertía en otra cosa válida</b> o se persistía tal cual para reventar mucho
/// más tarde, al aprovisionar el contenedor. El fallo aparecía entonces lejos de su causa y con la
/// configuración ya guardada, que es la peor combinación posible para diagnosticarlo.</para>
/// </summary>
public sealed class InstanceInputValidationTests
{
    private static Port ContainerPort(int value) => Port.Create(value).Value;

    // ---------- Protocolo (#56) ----------

    [Theory]
    [InlineData("tcp", PortProtocol.Tcp)]
    [InlineData("TCP", PortProtocol.Tcp)]
    [InlineData(" tcp ", PortProtocol.Tcp)]
    [InlineData("udp", PortProtocol.Udp)]
    [InlineData("UDP", PortProtocol.Udp)]
    public void Supported_protocols_parse(string input, PortProtocol expected)
    {
        PortMapping.TryParseProtocol(input, out var protocol).Should().BeTrue();
        protocol.Should().Be(expected);
    }

    /// <summary>
    /// El caso que da nombre al issue: antes, todo lo que no fuera exactamente <c>tcp</c> caía a
    /// UDP. Un typo publicaba el puerto en otro transporte sin avisar, y el servicio quedaba
    /// inalcanzable por un motivo que no aparecía en ninguna parte. Convertir una entrada inválida
    /// en otra válida distinta es peor que rechazarla.
    /// </summary>
    [Theory]
    [InlineData("tpc")]
    [InlineData("sctp")]
    [InlineData("http")]
    [InlineData("tcp6")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unsupported_protocols_are_rejected(string? input)
        => PortMapping.TryParseProtocol(input, out _).Should().BeFalse();

    // ---------- Puerto del host (#57) ----------

    [Theory]
    [InlineData(1)]
    [InlineData(8080)]
    [InlineData(65535)]
    public void A_host_port_inside_the_range_is_accepted(int hostPort)
        => new PortMapping(ContainerPort(80), hostPort).HostPort.Should().Be(hostPort);

    [Fact]
    public void A_null_host_port_is_valid_and_means_not_published()
        => new PortMapping(ContainerPort(80), null).HostPort.Should().BeNull();

    /// <summary>El cero no vale: en este dominio significaría "elige tú", que no es lo que se pide.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void A_host_port_outside_the_range_is_rejected(int hostPort)
    {
        var act = () => new PortMapping(ContainerPort(80), hostPort);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------- Healthcheck (#58) ----------

    [Fact]
    public void A_sane_healthcheck_is_accepted()
    {
        var hc = new Healthcheck(["CMD", "curl", "-f", "http://localhost:8080/health"], 30, 3, 5, 60);

        hc.IntervalSeconds.Should().Be(30);
        hc.Retries.Should().Be(3);
        hc.TimeoutSeconds.Should().Be(5);
        hc.StartPeriodSeconds.Should().Be(60);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_interval_is_rejected(int interval)
    {
        var act = () => new Healthcheck(["CMD", "true"], interval, 3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_retry_count_is_rejected(int retries)
    {
        var act = () => new Healthcheck(["CMD", "true"], 30, retries);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_non_positive_timeout_is_rejected()
    {
        var act = () => new Healthcheck(["CMD", "true"], 30, 3, TimeoutSeconds: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Un timeout ausente es legítimo —deja el valor por defecto del runtime—, a diferencia de uno
    /// puesto a cero, que es una configuración imposible.
    /// </summary>
    [Fact]
    public void An_absent_timeout_is_allowed()
        => new Healthcheck(["CMD", "true"], 30, 3, TimeoutSeconds: null).TimeoutSeconds.Should().BeNull();

    /// <summary>
    /// El período de gracia SÍ admite cero: "sin gracia" es una elección razonable. Lo que no cabe
    /// es que sea negativo. La diferencia con el intervalo no es caprichosa: cero segundos de gracia
    /// describe un comportamiento, cero segundos de intervalo no describe ninguno.
    /// </summary>
    [Fact]
    public void A_zero_start_period_is_allowed()
        => new Healthcheck(["CMD", "true"], 30, 3, StartPeriodSeconds: 0).StartPeriodSeconds.Should().Be(0);

    [Fact]
    public void A_negative_start_period_is_rejected()
    {
        var act = () => new Healthcheck(["CMD", "true"], 30, 3, StartPeriodSeconds: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Sin comando no hay comprobación: el contenedor quedaría sin healthcheck real mientras
    /// aparenta tener uno configurado, que es justo el tipo de verde falso que hay que evitar.
    /// </summary>
    [Fact]
    public void An_empty_test_command_is_rejected()
    {
        var act = () => new Healthcheck([], 30, 3);
        act.Should().Throw<ArgumentException>();
    }
}
