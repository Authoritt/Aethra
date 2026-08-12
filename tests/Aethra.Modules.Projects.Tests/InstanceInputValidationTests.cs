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
    public void A_host_port_inside_the_range_is_valid(int hostPort)
        => PortMapping.IsValidHostPort(hostPort).Should().BeTrue();

    [Fact]
    public void A_null_host_port_is_valid_and_means_not_published()
        => PortMapping.IsValidHostPort(null).Should().BeTrue();

    /// <summary>El cero no vale: en este dominio significaría "elige tú", que no es lo que se pide.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void A_host_port_outside_the_range_is_invalid(int hostPort)
        => PortMapping.IsValidHostPort(hostPort).Should().BeFalse();

    /// <summary>
    /// La comprobación NO está en el constructor, y este test lo fija. EF materializa el record por
    /// su constructor posicional al leer de la base: una guarda ahí haría que una fila escrita por
    /// el código anterior —cuando estos valores se aceptaban— reventara al LEERLA, devolviendo un
    /// 500 al listar instancias y dejando al usuario sin poder corregir la configuración, porque
    /// para corregirla hay que poder leerla.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Materialising_a_legacy_row_with_an_invalid_host_port_does_not_throw(int legacyHostPort)
    {
        var act = () => new PortMapping(ContainerPort(80), legacyHostPort);
        act.Should().NotThrow();
    }

    // ---------- Healthcheck (#58) ----------

    private static string? ValidateHc(
        IReadOnlyList<string>? test, int interval, int retries, int? timeout = null, int? startPeriod = null)
        => Healthcheck.Validate(test, interval, retries, timeout, startPeriod);

    [Fact]
    public void A_sane_healthcheck_is_valid()
        => ValidateHc(["CMD", "curl", "-f", "http://localhost:8080/health"], 30, 3, 5, 60).Should().BeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_interval_is_rejected(int interval)
        => ValidateHc(["CMD", "true"], interval, 3).Should().NotBeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_retry_count_is_rejected(int retries)
        => ValidateHc(["CMD", "true"], 30, retries).Should().NotBeNull();

    [Fact]
    public void A_non_positive_timeout_is_rejected()
        => ValidateHc(["CMD", "true"], 30, 3, timeout: 0).Should().NotBeNull();

    /// <summary>
    /// Un timeout ausente es legítimo —deja el valor por defecto del runtime—, a diferencia de uno
    /// puesto a cero, que es una configuración imposible.
    /// </summary>
    [Fact]
    public void An_absent_timeout_is_allowed()
        => ValidateHc(["CMD", "true"], 30, 3, timeout: null).Should().BeNull();

    /// <summary>
    /// El período de gracia SÍ admite cero: "sin gracia" es una elección razonable. Lo que no cabe
    /// es que sea negativo. La diferencia con el intervalo no es caprichosa: cero segundos de gracia
    /// describe un comportamiento, cero segundos de intervalo no describe ninguno.
    /// </summary>
    [Fact]
    public void A_zero_start_period_is_allowed()
        => ValidateHc(["CMD", "true"], 30, 3, startPeriod: 0).Should().BeNull();

    [Fact]
    public void A_negative_start_period_is_rejected()
        => ValidateHc(["CMD", "true"], 30, 3, startPeriod: -1).Should().NotBeNull();

    /// <summary>
    /// Sin comando no hay comprobación: el contenedor quedaría sin healthcheck real mientras
    /// aparenta tener uno configurado, que es justo el tipo de verde falso que hay que evitar.
    /// </summary>
    [Theory]
    [InlineData(null)]
    public void An_empty_test_command_is_rejected(IReadOnlyList<string>? test)
        => ValidateHc(test, 30, 3).Should().NotBeNull();

    [Fact]
    public void An_empty_test_list_is_rejected()
        => ValidateHc([], 30, 3).Should().NotBeNull();

    /// <summary>
    /// Igual que con los puertos: leer una instancia guardada con un healthcheck que hoy no
    /// aceptaríamos no puede reventar. La invariante se aplica en la entrada, no al materializar.
    /// </summary>
    [Fact]
    public void Materialising_a_legacy_healthcheck_does_not_throw()
    {
        var act = () => new Healthcheck([], 0, 0, 0, -1);
        act.Should().NotThrow();
    }
}
