using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;

/// <summary>
/// F13.9 — devuelve el túnel gestionado (el primero registrado) + su config de ingress REMOTA
/// actual leída del API. Devuelve null si no hay túnel registrado. Lo usa la UI guiada.
/// </summary>
public sealed record GetTunnelQuery : IQuery<CloudflareTunnelDto?>;

internal sealed class GetTunnelHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec)
    : IQueryHandler<GetTunnelQuery, CloudflareTunnelDto?>
{
    public async Task<Result<CloudflareTunnelDto?>> Handle(GetTunnelQuery request, CancellationToken cancellationToken)
    {
        var (tunnel, token) = await TunnelIngressSupport.LoadAsync(db, codec, cancellationToken).ConfigureAwait(false);
        if (tunnel is null)
        {
            return Result<CloudflareTunnelDto?>.Success(null);
        }

        var ingress = new List<TunnelIngressRuleDto>();
        try
        {
            var rules = await api.GetTunnelIngressAsync(tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false);
            ingress = rules.Select(r => new TunnelIngressRuleDto(r.Hostname, r.Service, r.NoTlsVerify)).ToList();
        }
        catch (CloudflareApiException)
        {
            // El token podría haberse revocado; devolvemos el túnel sin ingress (la UI lo marca).
        }

        return Result<CloudflareTunnelDto?>.Success(RegisterTunnelHandler.ToDto(tunnel, ingress));
    }
}
