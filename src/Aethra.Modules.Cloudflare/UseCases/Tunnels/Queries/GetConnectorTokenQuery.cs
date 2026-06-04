using System.Globalization;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;

/// <summary>
/// F13.11 — devuelve el connector token del túnel registrado (para correr cloudflared con
/// <c>TUNNEL_TOKEN</c>). Es un secreto; solo lo consume el host para arrancar el connector.
/// </summary>
public sealed record GetConnectorTokenQuery : IQuery<string>;

internal sealed class GetConnectorTokenHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec)
    : IQueryHandler<GetConnectorTokenQuery, string>
{
    public async Task<Result<string>> Handle(GetConnectorTokenQuery request, CancellationToken cancellationToken)
    {
        var (tunnel, token) = await TunnelIngressSupport.LoadAsync(db, codec, cancellationToken).ConfigureAwait(false);
        if (tunnel is null)
        {
            return Error.NotFound("tunnel.none", "No hay túnel registrado.");
        }
        try
        {
            var connectorToken = await api.GetTunnelConnectorTokenAsync(
                tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false);
            return connectorToken;
        }
        catch (CloudflareApiException ex)
        {
            return Error.Failure("cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"No se pudo obtener el connector token (code {ex.Code}): {ex.Message}"));
        }
    }
}
