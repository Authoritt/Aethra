using System.Globalization;
using Aethra.Modules.Cloudflare.Domain;
using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;

/// <summary>
/// F13.9 — asegura que el túnel tenga una regla de ingress para <paramref name="Hostname"/> →
/// el servicio de Aethra (proxy del central), insertada ANTES del catch-all. Idempotente: si ya
/// existe, no hace PUT (cero llamadas). Si no hay túnel registrado, es no-op (best-effort).
/// cloudflared aplica el cambio remoto sin reiniciar ⇒ cero blip.
/// </summary>
public sealed record EnsureTunnelHostnameCommand(string Hostname) : ICommand;

/// <summary>F13.9 — quita la(s) regla(s) de ingress de un hostname del túnel.</summary>
public sealed record RemoveTunnelHostnameCommand(string Hostname) : ICommand;

/// <summary>
/// F13.9 — reemplaza TODA la config de ingress del túnel (migración / import inicial desde el yaml
/// local). El último elemento debe ser el catch-all (Hostname null) o se agrega desde FallbackService.
/// </summary>
public sealed record SetTunnelIngressCommand(IReadOnlyList<TunnelIngressRule> Ingress) : ICommand;

internal static class TunnelIngressSupport
{
    public static async Task<(CloudflareTunnel? Tunnel, string Token)> LoadAsync(
        CloudflareDbContext db, ICloudflareTokenCodec codec, CancellationToken ct)
    {
        var tunnel = await db.Tunnels.OrderBy(t => t.CreatedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return tunnel is null ? (null, string.Empty) : (tunnel, codec.Decode(tunnel.ApiTokenCipher));
    }

    /// <summary>Garantiza que la lista termine en un catch-all (Hostname null).</summary>
    public static List<TunnelIngressRule> WithCatchAll(List<TunnelIngressRule> rules, CloudflareTunnel tunnel)
    {
        if (rules.Count == 0 || rules[^1].Hostname is not null)
        {
            var svc = string.IsNullOrWhiteSpace(tunnel.FallbackService) ? "http_status:404" : tunnel.FallbackService;
            rules.Add(new TunnelIngressRule(null, svc, tunnel.FallbackNoTlsVerify));
        }
        return rules;
    }
}

internal sealed class EnsureTunnelHostnameHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec, IClock clock)
    : ICommandHandler<EnsureTunnelHostnameCommand>
{
    public async Task<Result> Handle(EnsureTunnelHostnameCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            return Error.Validation("tunnel.invalid_hostname", "hostname requerido.");
        }
        var (tunnel, token) = await TunnelIngressSupport.LoadAsync(db, codec, cancellationToken).ConfigureAwait(false);
        if (tunnel is null)
        {
            return Result.Success(); // no-op: no hay túnel gestionado remoto aún.
        }
        var host = request.Hostname.Trim();

        try
        {
            var current = (await api.GetTunnelIngressAsync(tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false)).ToList();
            if (current.Any(r => string.Equals(r.Hostname, host, StringComparison.OrdinalIgnoreCase)))
            {
                return Result.Success(); // ya existe → sin PUT, sin blip.
            }
            // Insertar antes del primer catch-all (Hostname null); si no hay, antes del final.
            var idx = current.FindIndex(r => r.Hostname is null);
            var rule = new TunnelIngressRule(host, tunnel.AethraService, false);
            if (idx >= 0) { current.Insert(idx, rule); } else { current.Add(rule); }
            var finalRules = TunnelIngressSupport.WithCatchAll(current, tunnel);

            await api.PutTunnelIngressAsync(tunnel.AccountId, tunnel.TunnelId, token, finalRules, cancellationToken).ConfigureAwait(false);
            tunnel.MarkSynced(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (CloudflareApiException ex)
        {
            return Error.Failure("cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"No se pudo asegurar ingress (code {ex.Code}): {ex.Message}"));
        }
    }
}

internal sealed class RemoveTunnelHostnameHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec, IClock clock)
    : ICommandHandler<RemoveTunnelHostnameCommand>
{
    public async Task<Result> Handle(RemoveTunnelHostnameCommand request, CancellationToken cancellationToken)
    {
        var (tunnel, token) = await TunnelIngressSupport.LoadAsync(db, codec, cancellationToken).ConfigureAwait(false);
        if (tunnel is null)
        {
            return Result.Success();
        }
        var host = request.Hostname?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return Error.Validation("tunnel.invalid_hostname", "hostname requerido.");
        }
        try
        {
            var current = (await api.GetTunnelIngressAsync(tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false)).ToList();
            var kept = current.Where(r => !string.Equals(r.Hostname, host, StringComparison.OrdinalIgnoreCase)).ToList();
            if (kept.Count == current.Count)
            {
                return Result.Success(); // nada que quitar.
            }
            var finalRules = TunnelIngressSupport.WithCatchAll(kept, tunnel);
            await api.PutTunnelIngressAsync(tunnel.AccountId, tunnel.TunnelId, token, finalRules, cancellationToken).ConfigureAwait(false);
            tunnel.MarkSynced(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (CloudflareApiException ex)
        {
            return Error.Failure("cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"No se pudo quitar ingress (code {ex.Code}): {ex.Message}"));
        }
    }
}

internal sealed class SetTunnelIngressHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec, IClock clock)
    : ICommandHandler<SetTunnelIngressCommand>
{
    public async Task<Result> Handle(SetTunnelIngressCommand request, CancellationToken cancellationToken)
    {
        var (tunnel, token) = await TunnelIngressSupport.LoadAsync(db, codec, cancellationToken).ConfigureAwait(false);
        if (tunnel is null)
        {
            return Error.NotFound("tunnel.none", "No hay túnel gestionado registrado.");
        }
        var rules = TunnelIngressSupport.WithCatchAll((request.Ingress ?? []).ToList(), tunnel);
        try
        {
            await api.PutTunnelIngressAsync(tunnel.AccountId, tunnel.TunnelId, token, rules, cancellationToken).ConfigureAwait(false);
            tunnel.MarkSynced(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (CloudflareApiException ex)
        {
            return Error.Failure("cloudflare.api_error",
                string.Create(CultureInfo.InvariantCulture, $"No se pudo aplicar ingress (code {ex.Code}): {ex.Message}"));
        }
    }
}
