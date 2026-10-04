using Aethra.Modules.Cloudflare.Domain;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels;

/// <summary>
/// OT-008 T15: regla unica para el parametro <c>mode</c> de las tools MCP y endpoints de tunel. Los clientes
/// MCP rellenan los parametros anulables "requeridos" con el literal <c>"null"</c> (o <c>"none"</c>...) en vez
/// de omitirlos; eso es "sin modo" (toma el default), no un modo invalido. Solo un valor que no es ni vacio, ni
/// un literal de ausencia, ni un modo conocido es <c>cloudflare.tunnel_mode_invalid</c>.
/// </summary>
public static class TunnelModeParser
{
    public static Result<TunnelMode?> Parse(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return Result<TunnelMode?>.Success(null);
        }

        return mode.Trim().ToLowerInvariant().Replace("-", "_") switch
        {
            "null" or "none" or "undefined" or "default" => Result<TunnelMode?>.Success(null),
            "centralproxy" or "central_proxy" => Result<TunnelMode?>.Success(TunnelMode.CentralProxy),
            "direct" => Result<TunnelMode?>.Success(TunnelMode.Direct),
            _ => Error.Validation("cloudflare.tunnel_mode_invalid", "Modo de túnel no soportado."),
        };
    }
}
