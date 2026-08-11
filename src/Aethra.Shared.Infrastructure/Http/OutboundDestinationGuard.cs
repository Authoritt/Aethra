using System.Net;
using System.Net.Sockets;
using Aethra.Shared.Kernel.Net;
using Microsoft.Extensions.Options;

namespace Aethra.Shared.Infrastructure.Http;

/// <summary>
/// Política de destinos para las peticiones que ORIGINA el plano de control (monitores HTTP,
/// webhooks de notificación, <c>git clone</c> de discovery).
///
/// <para>Sección de configuración <c>OutboundDestinations</c>.</para>
/// </summary>
public sealed class OutboundDestinationOptions
{
    /// <summary>
    /// Si es <c>false</c>, la política no bloquea nada y solo registra. Pensado para que un
    /// operador con destinos internos legítimos pueda desplegar, mirar los avisos y afinar
    /// <see cref="AllowedHosts"/> antes de activarla. El valor por defecto es <b>bloquear</b>:
    /// una política de seguridad que llega apagada por defecto no protege a nadie.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hosts exentos, comparados sin distinguir mayúsculas. Es la vía para permitir un destino
    /// interno a propósito (un servicio de la malla que de verdad hay que vigilar) sin abrir todo
    /// el rango al que pertenece. Acepta el nombre tal cual, no comodines: una excepción debe ser
    /// explícita y nominal, porque cada entrada aquí es un agujero consentido.
    /// </summary>
    public IList<string> AllowedHosts { get; } = [];
}

/// <summary>Veredicto sobre un destino.</summary>
/// <param name="Allowed">Si se puede pedir.</param>
/// <param name="Reason">Motivo legible cuando no se puede; <c>null</c> si se permite.</param>
public readonly record struct DestinationVerdict(bool Allowed, string? Reason)
{
    public static DestinationVerdict Allow() => new(true, null);

    public static DestinationVerdict Deny(string reason) => new(false, reason);
}

/// <summary>Decide si el plano de control puede emitir una petición hacia un destino.</summary>
public interface IOutboundDestinationGuard
{
    /// <summary>
    /// Comprueba una URI completa: resuelve su host y clasifica TODAS las direcciones obtenidas.
    /// </summary>
    Task<DestinationVerdict> CheckAsync(Uri uri, CancellationToken ct);

    /// <summary>Comprueba un host suelto (para destinos que no son URI, como el SCP de git).</summary>
    Task<DestinationVerdict> CheckHostAsync(string host, CancellationToken ct);

    /// <summary>
    /// Comprueba una dirección ya resuelta, en el momento de conectar. Es lo que cierra el
    /// <b>DNS rebinding</b>: entre la validación y la conexión, un nombre bajo control del atacante
    /// puede cambiar de una IP pública a una interna, y solo mirando la dirección a la que de verdad
    /// se abre el socket se detecta.
    /// </summary>
    DestinationVerdict CheckResolvedAddress(IPAddress address, string host);
}

/// <inheritdoc cref="IOutboundDestinationGuard"/>
public sealed class OutboundDestinationGuard(IOptions<OutboundDestinationOptions> options)
    : IOutboundDestinationGuard
{
    private OutboundDestinationOptions Options => options.Value;

    public async Task<DestinationVerdict> CheckAsync(Uri uri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return await CheckHostAsync(uri.IdnHost, ct).ConfigureAwait(false);
    }

    public async Task<DestinationVerdict> CheckHostAsync(string host, CancellationToken ct)
    {
        if (!Options.Enabled || IsAllowlisted(host))
        {
            return DestinationVerdict.Allow();
        }
        if (string.IsNullOrWhiteSpace(host))
        {
            return DestinationVerdict.Deny("el destino no tiene host.");
        }

        // Un literal IP no necesita DNS, y hay que clasificarlo igual: escribir la IP a mano es la
        // forma más directa de intentar el salto.
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return Verdict(literal, host);
        }

        IPAddress[] resolved;
        try
        {
            resolved = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            return DestinationVerdict.Deny($"no se pudo resolver el host '{host}': {ex.Message}");
        }

        if (resolved.Length == 0)
        {
            return DestinationVerdict.Deny($"el host '{host}' no resolvió a ninguna dirección.");
        }

        // TODAS las direcciones, no la primera. Un nombre puede resolver a una pública y a una
        // interna a la vez; quedarse con la primera deja que el atacante gane con reintentar, porque
        // el orden de resolución no está garantizado.
        foreach (var address in resolved)
        {
            var verdict = Verdict(address, host);
            if (!verdict.Allowed)
            {
                return verdict;
            }
        }
        return DestinationVerdict.Allow();
    }

    public DestinationVerdict CheckResolvedAddress(IPAddress address, string host)
    {
        if (!Options.Enabled || IsAllowlisted(host))
        {
            return DestinationVerdict.Allow();
        }
        return Verdict(address, host);
    }

    private DestinationVerdict Verdict(IPAddress address, string host)
    {
        var risk = DestinationAddressRules.Classify(address);
        return risk == DestinationRisk.None
            ? DestinationVerdict.Allow()
            : DestinationVerdict.Deny(
                $"'{host}' apunta a {address}, que es {DestinationAddressRules.Describe(risk)}. "
                + "El plano de control no emite peticiones hacia ahí. Si es un destino interno "
                + "legítimo, añádelo a OutboundDestinations:AllowedHosts.");
    }

    private bool IsAllowlisted(string host)
        => !string.IsNullOrWhiteSpace(host)
            && Options.AllowedHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
}
