using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;

/// <summary>Devuelve el tunel Direct registrado para una VM satelite, si existe.</summary>
public sealed record GetTunnelForVmQuery(string TargetVmId) : IQuery<CloudflareTunnelDto?>;

internal sealed class GetTunnelForVmHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec)
    : IQueryHandler<GetTunnelForVmQuery, CloudflareTunnelDto?>
{
    public async Task<Result<CloudflareTunnelDto?>> Handle(GetTunnelForVmQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TargetVmId))
        {
            return Error.Validation("tunnel.invalid_target_vm", "target_vm_id requerido.");
        }

        var (tunnel, token) = await TunnelIngressSupport
            .LoadForVmAsync(db, codec, request.TargetVmId, cancellationToken)
            .ConfigureAwait(false);
        if (tunnel is null)
        {
            return Result<CloudflareTunnelDto?>.Success(null);
        }

        var ingress = new List<TunnelIngressRuleDto>();
        try
        {
            var rules = await api.GetTunnelIngressAsync(
                tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false);
            ingress = rules.Select(r => new TunnelIngressRuleDto(r.Hostname, r.Service, r.NoTlsVerify)).ToList();
        }
        catch (CloudflareApiException)
        {
            // El token podria haberse revocado; devolvemos el tunel sin ingress (la UI lo marca).
        }

        return Result<CloudflareTunnelDto?>.Success(RegisterTunnelHandler.ToDto(tunnel, ingress));
    }
}
