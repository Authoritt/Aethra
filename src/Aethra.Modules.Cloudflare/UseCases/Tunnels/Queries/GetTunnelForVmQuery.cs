using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels.Queries;

/// <summary>
/// Devuelve el tunel Direct registrado para una VM satelite, si existe.
/// El runner usa <see cref="IncludeIngress"/> en <c>false</c> para resolver colocacion solo desde BD,
/// sin llamar al API de Cloudflare antes de tocar contenedores.
/// </summary>
public sealed record GetTunnelForVmQuery(string TargetVmId, bool IncludeIngress = true) : IQuery<CloudflareTunnelDto?>;

/// <summary>
/// Devuelve el tunel CentralProxy historico. Con <see cref="IncludeIngress"/> en <c>false</c> es una
/// consulta ligera de metadata (incluye TargetVmId) y no llama al API de Cloudflare.
/// </summary>
public sealed record GetCentralProxyTunnelQuery(bool IncludeIngress = true) : IQuery<CloudflareTunnelDto?>;

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
        if (request.IncludeIngress)
        {
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
        }

        return Result<CloudflareTunnelDto?>.Success(RegisterTunnelHandler.ToDto(tunnel, ingress));
    }
}

internal sealed class GetCentralProxyTunnelHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec)
    : IQueryHandler<GetCentralProxyTunnelQuery, CloudflareTunnelDto?>
{
    public async Task<Result<CloudflareTunnelDto?>> Handle(
        GetCentralProxyTunnelQuery request,
        CancellationToken cancellationToken)
    {
        var (tunnel, token) = await TunnelIngressSupport
            .LoadAsync(db, codec, cancellationToken)
            .ConfigureAwait(false);
        if (tunnel is null)
        {
            return Result<CloudflareTunnelDto?>.Success(null);
        }

        var ingress = new List<TunnelIngressRuleDto>();
        if (request.IncludeIngress)
        {
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
        }

        return Result<CloudflareTunnelDto?>.Success(RegisterTunnelHandler.ToDto(tunnel, ingress));
    }
}
