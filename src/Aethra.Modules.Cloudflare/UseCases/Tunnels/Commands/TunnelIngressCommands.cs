using System.Collections.Concurrent;
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
/// F13.9 - asegura que el tunel tenga una regla de ingress para <paramref name="Hostname"/>.
/// Con argumentos nulos conserva el flujo historico: primer tunel CentralProxy por CreatedAt y
/// servicio AethraService; en Direct permite reemplazar la regla cuando el servicio cambia.
/// </summary>
public sealed record EnsureTunnelHostnameCommand(
    string Hostname,
    string? TunnelId = null,
    string? Service = null) : ICommand;

/// <summary>F13.9 - quita la(s) regla(s) de ingress de un hostname del tunel.</summary>
public sealed record RemoveTunnelHostnameCommand(string Hostname, string? TunnelId = null) : ICommand;

/// <summary>
/// F13.9 - reemplaza TODA la config de ingress del tunel (migracion / import inicial desde el yaml
/// local). El ultimo elemento debe ser el catch-all (Hostname null) o se agrega desde FallbackService.
/// </summary>
public sealed record SetTunnelIngressCommand(
    IReadOnlyList<TunnelIngressRule> Ingress,
    string? TunnelId = null) : ICommand;

internal static class TunnelIngressSupport
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TunnelLocks = new(StringComparer.Ordinal);

    public static async Task<(CloudflareTunnel? Tunnel, string Token)> LoadAsync(
        CloudflareDbContext db,
        ICloudflareTokenCodec codec,
        CancellationToken ct,
        string? tunnelId = null,
        bool includeToken = true)
    {
        var normalizedTunnelId = string.IsNullOrWhiteSpace(tunnelId) ? null : tunnelId.Trim();
        var tunnel = normalizedTunnelId is null
            ? await db.Tunnels
                .Where(t => t.Mode == TunnelMode.CentralProxy)
                .OrderBy(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
            : await db.Tunnels
                .FirstOrDefaultAsync(t => t.TunnelId == normalizedTunnelId, ct)
                .ConfigureAwait(false);

        return tunnel is null || !includeToken
            ? (tunnel, string.Empty)
            : (tunnel, codec.Decode(tunnel.ApiTokenCipher));
    }

    public static async Task<(CloudflareTunnel? Tunnel, string Token)> LoadForVmAsync(
        CloudflareDbContext db,
        ICloudflareTokenCodec codec,
        string targetVmId,
        CancellationToken ct,
        bool includeToken = true)
    {
        var normalizedTargetVmId = targetVmId.Trim();
        var tunnel = await db.Tunnels
            .Where(t => t.Mode == TunnelMode.Direct && t.TargetVmId == normalizedTargetVmId)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return tunnel is null || !includeToken
            ? (tunnel, string.Empty)
            : (tunnel, codec.Decode(tunnel.ApiTokenCipher));
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

    public static TimeSpan? LockTimeoutFor(string? tunnelId)
        => string.IsNullOrWhiteSpace(tunnelId) ? null : LockTimeout;

    public static async Task<Result> RunSerializedAsync(
        CloudflareTunnel tunnel,
        Func<Task<Result>> action,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var gate = TunnelLocks.GetOrAdd(tunnel.TunnelId, _ => new SemaphoreSlim(1, 1));
        if (timeout is { } waitTimeout)
        {
            var entered = await gate.WaitAsync(waitTimeout, cancellationToken).ConfigureAwait(false);
            if (!entered)
            {
                return Error.Failure(
                    "cloudflare.ingress_lock_timeout",
                    string.Create(CultureInfo.InvariantCulture, $"Timeout esperando el lock de ingress del tunel {tunnel.TunnelId}."));
            }
        }
        else
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public static bool ContainsRule(
        IReadOnlyList<TunnelIngressRule> rules,
        string host,
        string service,
        bool noTlsVerify = false)
        => rules.Any(r =>
            string.Equals(r.Hostname, host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Service, service, StringComparison.Ordinal)
            && r.NoTlsVerify == noTlsVerify);

    public static bool ContainsHost(IReadOnlyList<TunnelIngressRule> rules, string host)
        => rules.Any(r => string.Equals(r.Hostname, host, StringComparison.OrdinalIgnoreCase));

    public static bool ContainsAllRules(
        IReadOnlyList<TunnelIngressRule> rules,
        IReadOnlyList<TunnelIngressRule> expected)
        => expected.All(rule => rules.Any(r =>
            string.Equals(r.Hostname, rule.Hostname, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Service, rule.Service, StringComparison.Ordinal)
            && r.NoTlsVerify == rule.NoTlsVerify));
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

        var (tunnel, token) = await TunnelIngressSupport
            .LoadAsync(db, codec, cancellationToken, request.TunnelId)
            .ConfigureAwait(false);
        if (tunnel is null)
        {
            if (!string.IsNullOrWhiteSpace(request.TunnelId))
            {
                return Error.NotFound("tunnel.not_found", $"Tunel '{request.TunnelId.Trim()}' no existe.");
            }
            return Result.Success();
        }

        var host = request.Hostname.Trim();
        var service = string.IsNullOrWhiteSpace(request.Service) ? tunnel.AethraService : request.Service.Trim();

        return await TunnelIngressSupport.RunSerializedAsync(tunnel, async () =>
        {
            try
            {
                var current = (await api.GetTunnelIngressAsync(
                    tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false)).ToList();

                var plan = TunnelIngressPlanner.Plan(current, host, service);
                if (plan.Action == TunnelIngressPlanAction.NoOp
                    || (tunnel.Mode == TunnelMode.CentralProxy && TunnelIngressSupport.ContainsHost(current, host)))
                {
                    return Result.Success();
                }

                if (plan.Action == TunnelIngressPlanAction.Replace && tunnel.Mode != TunnelMode.Direct)
                {
                    return Result.Success();
                }

                var finalRules = TunnelIngressSupport.WithCatchAll(plan.Ingress.ToList(), tunnel);
                await api.PutTunnelIngressAsync(
                    tunnel.AccountId, tunnel.TunnelId, token, finalRules, cancellationToken).ConfigureAwait(false);

                var verified = await api.GetTunnelIngressAsync(
                    tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false);
                if (!TunnelIngressSupport.ContainsRule(verified, host, service))
                {
                    return Error.Failure(
                        "cloudflare.ingress_not_applied",
                        string.Create(CultureInfo.InvariantCulture, $"La regla de ingress para {host} no aparece tras releer el tunel {tunnel.TunnelId}."));
                }

                tunnel.MarkSynced(clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Result.Success();
            }
            catch (CloudflareApiException ex)
            {
                return Error.Failure("cloudflare.api_error",
                    string.Create(CultureInfo.InvariantCulture, $"No se pudo asegurar ingress (code {ex.Code}): {ex.Message}"));
            }
        }, cancellationToken, TunnelIngressSupport.LockTimeoutFor(request.TunnelId)).ConfigureAwait(false);
    }
}

internal sealed class RemoveTunnelHostnameHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec, IClock clock)
    : ICommandHandler<RemoveTunnelHostnameCommand>
{
    public async Task<Result> Handle(RemoveTunnelHostnameCommand request, CancellationToken cancellationToken)
    {
        var (tunnel, token) = await TunnelIngressSupport
            .LoadAsync(db, codec, cancellationToken, request.TunnelId)
            .ConfigureAwait(false);
        if (tunnel is null)
        {
            if (!string.IsNullOrWhiteSpace(request.TunnelId))
            {
                return Error.NotFound("tunnel.not_found", $"Tunel '{request.TunnelId.Trim()}' no existe.");
            }
            return Result.Success();
        }

        var host = request.Hostname?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return Error.Validation("tunnel.invalid_hostname", "hostname requerido.");
        }

        return await TunnelIngressSupport.RunSerializedAsync(tunnel, async () =>
        {
            try
            {
                var current = (await api.GetTunnelIngressAsync(
                    tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false)).ToList();
                var plan = TunnelIngressPlanner.PlanRemove(current, host);
                if (plan.Action == TunnelIngressPlanAction.NoOp)
                {
                    return Result.Success();
                }

                var finalRules = TunnelIngressSupport.WithCatchAll(plan.Ingress.ToList(), tunnel);
                await api.PutTunnelIngressAsync(
                    tunnel.AccountId, tunnel.TunnelId, token, finalRules, cancellationToken).ConfigureAwait(false);

                var verified = await api.GetTunnelIngressAsync(
                    tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false);
                if (TunnelIngressSupport.ContainsHost(verified, host))
                {
                    return Error.Failure(
                        "cloudflare.ingress_not_applied",
                        string.Create(CultureInfo.InvariantCulture, $"La regla de ingress para {host} sigue presente tras releer el tunel {tunnel.TunnelId}."));
                }

                tunnel.MarkSynced(clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Result.Success();
            }
            catch (CloudflareApiException ex)
            {
                return Error.Failure("cloudflare.api_error",
                    string.Create(CultureInfo.InvariantCulture, $"No se pudo quitar ingress (code {ex.Code}): {ex.Message}"));
            }
        }, cancellationToken, TunnelIngressSupport.LockTimeoutFor(request.TunnelId)).ConfigureAwait(false);
    }
}

internal sealed class SetTunnelIngressHandler(
    CloudflareDbContext db, ICloudflareApiClient api, ICloudflareTokenCodec codec, IClock clock)
    : ICommandHandler<SetTunnelIngressCommand>
{
    public async Task<Result> Handle(SetTunnelIngressCommand request, CancellationToken cancellationToken)
    {
        var (tunnel, token) = await TunnelIngressSupport
            .LoadAsync(db, codec, cancellationToken, request.TunnelId)
            .ConfigureAwait(false);
        if (tunnel is null)
        {
            return Error.NotFound("tunnel.none", "No hay tunel gestionado registrado.");
        }

        var rules = TunnelIngressSupport.WithCatchAll((request.Ingress ?? []).ToList(), tunnel);
        return await TunnelIngressSupport.RunSerializedAsync(tunnel, async () =>
        {
            try
            {
                await api.PutTunnelIngressAsync(
                    tunnel.AccountId, tunnel.TunnelId, token, rules, cancellationToken).ConfigureAwait(false);

                var verified = await api.GetTunnelIngressAsync(
                    tunnel.AccountId, tunnel.TunnelId, token, cancellationToken).ConfigureAwait(false);
                if (!TunnelIngressSupport.ContainsAllRules(verified, rules))
                {
                    return Error.Failure(
                        "cloudflare.ingress_not_applied",
                        string.Create(CultureInfo.InvariantCulture, $"La configuracion de ingress no aparece completa tras releer el tunel {tunnel.TunnelId}."));
                }

                tunnel.MarkSynced(clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Result.Success();
            }
            catch (CloudflareApiException ex)
            {
                return Error.Failure("cloudflare.api_error",
                    string.Create(CultureInfo.InvariantCulture, $"No se pudo aplicar ingress (code {ex.Code}): {ex.Message}"));
            }
        }, cancellationToken, TunnelIngressSupport.LockTimeoutFor(request.TunnelId)).ConfigureAwait(false);
    }
}
