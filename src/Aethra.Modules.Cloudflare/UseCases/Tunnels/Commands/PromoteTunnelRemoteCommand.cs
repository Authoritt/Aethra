using System.Globalization;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;

/// <summary>
/// F13.10 — "promueve" el túnel a gestión REMOTA: lee la config de ingress actual y la re-PUTea, lo
/// que en Cloudflare cambia <c>source: local → cloudflare</c>. Es el paso de config que faltaba para
/// que correr el connector con <c>--token</c> aplique la config remota. Idempotente. NO toca el host
/// (el cambio del systemd unit a <c>--token</c> sigue siendo el único paso manual, 1-vez).
/// Devuelve el número de reglas promovidas.
/// </summary>
public sealed record PromoteTunnelRemoteCommand : ICommand<int>;

internal sealed class PromoteTunnelRemoteHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec, IClock clock)
    : ICommandHandler<PromoteTunnelRemoteCommand, int>
{
    public async Task<Result<int>> Handle(PromoteTunnelRemoteCommand request, CancellationToken cancellationToken)
    {
        var (tunnel, token) = await TunnelIngressSupport.LoadAsync(db, codec, cancellationToken).ConfigureAwait(false);
        if (tunnel is null)
        {
            return Error.NotFound("tunnel.none", "No hay túnel registrado. Conéctalo primero.");
        }
        try
        {
            var current = (await api.GetTunnelIngressAsync(tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false)).ToList();
            if (current.Count == 0)
            {
                return Error.Conflict("tunnel.empty_config",
                    "El túnel no tiene config de ingress en Cloudflare para promover. Define las reglas primero.");
            }
            var rules = TunnelIngressSupport.WithCatchAll(current, tunnel);
            await api.PutTunnelIngressAsync(tunnel.AccountId, tunnel.TunnelId, token, rules, cancellationToken).ConfigureAwait(false);
            tunnel.MarkSynced(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return rules.Count;
        }
        catch (CloudflareApiException ex)
        {
            return Error.Failure("cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"No se pudo promover (code {ex.Code}): {ex.Message}"));
        }
    }
}
