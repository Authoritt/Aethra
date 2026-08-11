using System.Net;
using Aethra.Shared.Kernel.Net;
using FluentAssertions;
using Xunit;

namespace Aethra.Shared.Kernel.Tests;

/// <summary>
/// Clasificación de direcciones para peticiones que origina el SERVIDOR (SSRF).
///
/// <para>Aethra corre en la misma red que lo que gestiona: alcanza la malla privada, el endpoint de
/// metadatos de la nube y los puertos no publicados. Cualquier función que acepte una URL del
/// llamante y la pida desde aquí convierte el plano de control en un proxy hacia todo eso.</para>
///
/// <para>Los casos negativos son los que importan: un falso "es pública" abre el agujero en
/// silencio, mientras que un falso "es privada" se nota enseguida porque rompe un caso legítimo.</para>
/// </summary>
public sealed class DestinationAddressRulesTests
{
    private static DestinationRisk Risk(string ip) => DestinationAddressRules.Classify(IPAddress.Parse(ip));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("140.82.121.4")]      // github.com
    [InlineData("2606:4700:4700::1111")]
    public void Public_addresses_are_routable(string ip)
    {
        Risk(ip).Should().Be(DestinationRisk.None);
        DestinationAddressRules.IsPubliclyRoutable(IPAddress.Parse(ip)).Should().BeTrue();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]         // todo 127/8 es loopback, no solo .0.1
    [InlineData("::1")]
    public void Loopback_is_detected(string ip) => Risk(ip).Should().Be(DestinationRisk.Loopback);

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("fd00::1")]           // unique local
    [InlineData("fc00::1")]
    public void Private_networks_are_detected(string ip) => Risk(ip).Should().Be(DestinationRisk.Private);

    /// <summary>
    /// Los bordes del rango 172.16/12 son el error clásico: 172.15 y 172.32 NO son privadas, y una
    /// comprobación perezosa de "empieza por 172" las bloquearía.
    /// </summary>
    [Theory]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    public void The_172_range_boundaries_are_public(string ip) => Risk(ip).Should().Be(DestinationRisk.None);

    /// <summary>
    /// El destino más valioso de un SSRF en la nube: el endpoint de metadatos de instancia, que
    /// suele entregar credenciales sin pedir autenticación.
    /// </summary>
    [Fact]
    public void The_cloud_metadata_endpoint_is_link_local()
        => Risk("169.254.169.254").Should().Be(DestinationRisk.LinkLocal);

    [Theory]
    [InlineData("169.254.0.1")]
    [InlineData("fe80::1")]
    public void Link_local_is_detected(string ip) => Risk(ip).Should().Be(DestinationRisk.LinkLocal);

    /// <summary>
    /// CGNAT es el rango de Tailscale, o sea la malla privada por la que estas VMs se hablan entre
    /// sí. Un SSRF que lo alcance llega a servicios que jamás se publicaron.
    /// </summary>
    [Theory]
    [InlineData("100.64.0.1")]
    [InlineData("100.116.223.31")]
    [InlineData("100.127.255.254")]
    public void Carrier_grade_nat_is_detected(string ip) => Risk(ip).Should().Be(DestinationRisk.CarrierGrade);

    /// <summary>Los bordes de 100.64/10: 100.63 y 100.128 son públicas.</summary>
    [Theory]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    public void The_cgnat_boundaries_are_public(string ip) => Risk(ip).Should().Be(DestinationRisk.None);

    /// <summary>
    /// <b>El bypass clásico.</b> Una IPv4 embebida en IPv6 apunta a la misma máquina que su IPv4;
    /// si no se desenvuelve, el filtro la evalúa como IPv6 cualquiera y deja pasar loopback y
    /// privadas. Es la forma más común de saltarse estas comprobaciones.
    /// </summary>
    [Theory]
    [InlineData("::ffff:127.0.0.1", DestinationRisk.Loopback)]
    [InlineData("::ffff:10.0.0.1", DestinationRisk.Private)]
    [InlineData("::ffff:169.254.169.254", DestinationRisk.LinkLocal)]
    [InlineData("::ffff:8.8.8.8", DestinationRisk.None)]
    public void IPv4_mapped_addresses_are_unwrapped_before_classifying(string ip, DestinationRisk expected)
        => Risk(ip).Should().Be(expected);

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void The_unspecified_address_is_rejected(string ip)
        => Risk(ip).Should().Be(DestinationRisk.Unspecified);

    [Theory]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("255.255.255.255")]
    [InlineData("ff02::1")]
    public void Multicast_and_broadcast_are_not_unicast_destinations(string ip)
        => Risk(ip).Should().Be(DestinationRisk.NonUnicast);

    [Theory]
    [InlineData("192.0.2.1")]         // TEST-NET-1
    [InlineData("198.51.100.1")]      // TEST-NET-2
    [InlineData("203.0.113.1")]       // TEST-NET-3
    [InlineData("198.18.0.1")]        // benchmarking
    [InlineData("240.0.0.1")]         // reservado
    [InlineData("2001:db8::1")]       // documentación IPv6
    public void Reserved_and_documentation_ranges_are_rejected(string ip)
        => Risk(ip).Should().Be(DestinationRisk.Reserved);

    /// <summary>Sin dirección no se puede afirmar que el destino sea seguro.</summary>
    [Fact]
    public void A_null_address_is_never_routable()
    {
        DestinationAddressRules.Classify(null).Should().Be(DestinationRisk.Reserved);
        DestinationAddressRules.IsPubliclyRoutable(null).Should().BeFalse();
    }

    /// <summary>
    /// El texto va a un mensaje de error que lee quien configuró la URL: tiene que explicar el
    /// motivo sin obligarle a saber qué es un rango CIDR.
    /// </summary>
    [Theory]
    [InlineData(DestinationRisk.Loopback)]
    [InlineData(DestinationRisk.Private)]
    [InlineData(DestinationRisk.LinkLocal)]
    [InlineData(DestinationRisk.CarrierGrade)]
    [InlineData(DestinationRisk.Unspecified)]
    [InlineData(DestinationRisk.NonUnicast)]
    [InlineData(DestinationRisk.Reserved)]
    public void Every_risk_has_a_human_explanation(DestinationRisk risk)
        => DestinationAddressRules.Describe(risk).Should().NotBeNullOrWhiteSpace();
}
