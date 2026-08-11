using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aethra.Shared.Infrastructure.Http;

/// <summary>
/// Cableado de la política de destinos a los <c>HttpClient</c> que emiten peticiones hacia URLs
/// elegidas por el llamante (sondas de monitor, webhooks de notificación).
/// </summary>
public static class OutboundDestinationHttpExtensions
{
    /// <summary>
    /// Registra la política para quien la consulta directamente, sin pasar por un <c>HttpClient</c>.
    /// Es el caso de los destinos que no son HTTP del proceso: <c>git clone</c> lanza un proceso
    /// externo que abre sus propios sockets, así que ahí solo se puede validar el host por
    /// adelantado (ver la nota de rebinding en <c>DiscoverTemplateHandler</c>).
    /// </summary>
    public static IServiceCollection AddOutboundDestinationGuard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<OutboundDestinationOptions>().BindConfiguration("OutboundDestinations");
        services.TryAddSingleton<IOutboundDestinationGuard, OutboundDestinationGuard>();
        return services;
    }

    /// <summary>
    /// Aplica la política en el <b>momento de abrir el socket</b>, no solo al validar la URL.
    ///
    /// <para>Validar la URL por adelantado no basta por sí solo: entre esa comprobación y la
    /// conexión, un nombre bajo control del atacante puede cambiar de una IP pública a una interna
    /// (<i>DNS rebinding</i>). Aquí se mira la dirección a la que de verdad se va a conectar, que es
    /// el único punto donde esa diferencia es observable.</para>
    ///
    /// <para>Se cablea en el handler y no en cada llamada a propósito: así cubre todas las rutas que
    /// usen este cliente, incluidas las futuras y las <b>redirecciones</b> —que son otra forma
    /// clásica de llegar a un destino interno partiendo de una URL pública inocente.</para>
    /// </summary>
    /// <param name="configure">
    /// Ajustes adicionales del handler. Existe porque este método configura el handler PRIMARIO y
    /// sobrescribiría cualquier otro registro: quien ya tenía opciones propias (por ejemplo el
    /// monitor, que no sigue redirecciones para no falsear el uptime) las pasa por aquí en vez de
    /// perderlas silenciosamente.
    /// </param>
    public static IHttpClientBuilder GuardOutboundDestinations(
        this IHttpClientBuilder builder, Action<SocketsHttpHandler>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // El guard se registra aquí, no en un Program.cs que haya que acordarse de tocar: quien pide
        // la protección la obtiene completa. Si faltara el registro, esto fallaría al construir el
        // cliente —en el arranque— en vez de dejar el cliente sin proteger, que es el modo de fallo
        // que importa evitar.
        builder.Services.AddOptions<OutboundDestinationOptions>().BindConfiguration("OutboundDestinations");
        builder.Services.TryAddSingleton<IOutboundDestinationGuard, OutboundDestinationGuard>();

        return builder.ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var guard = sp.GetRequiredService<IOutboundDestinationGuard>();
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, ct) =>
                {
                    var host = context.DnsEndPoint.Host;
                    var port = context.DnsEndPoint.Port;

                    var addresses = await System.Net.Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
                    foreach (var address in addresses)
                    {
                        var verdict = guard.CheckResolvedAddress(address, host);
                        if (!verdict.Allowed)
                        {
                            throw new HttpRequestException(
                                $"Destino bloqueado por la política de salida: {verdict.Reason}");
                        }
                    }

                    // Se conecta a las direcciones YA validadas, no volviendo a resolver el nombre:
                    // una segunda resolución podría devolver otra cosa y anular la comprobación.
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(addresses, port, ct).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };
            configure?.Invoke(handler);
            return handler;
        });
    }
}
