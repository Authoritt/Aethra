using Aethra.Modules.Proxy.Domain;
using Aethra.Shared.Contracts.Proxy;
using Aethra.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Proxy.Infrastructure.Tls;

/// <summary>
/// Hosted service que escanea cada hora los certificados con <c>Status = Issued</c> y
/// <c>RenewAfter &lt;= now</c>. Para cada uno invoca <see cref="ICertManager.RenewAsync"/>.
/// Si la renovación falla, se loguea y el cert vuelve a la siguiente pasada (en peor caso 6h).
/// Si el cert pasó <c>NotAfter</c> sin haberse renovado, se emite <see cref="CertificateExpiredEvent"/>.
/// </summary>
public sealed class CertRenewalWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<CertRenewalWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan NormalInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan BackoffInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CertRenewalWorker arrancando — intervalo normal {Normal}, backoff {Backoff}",
            NormalInterval, BackoffInterval);

        // Pequeño retraso inicial para no pegarle a la BD justo al boot.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // Lazo principal: capturar cualquier excepción para no matar al worker.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "CertRenewalWorker falló en el loop principal");
            }

            try
            {
                await Task.Delay(NormalInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICertificateStore>();
        var manager = scope.ServiceProvider.GetRequiredService<ICertManager>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter<ProxyDbContext>>();

        var now = clock.GetUtcNow();
        var due = await store.ListDueForRenewalAttemptAsync(now, ct).ConfigureAwait(false);
        if (due.Count == 0)
        {
            return;
        }

        logger.LogInformation("Renovando {Count} certificate(s) en este pase", due.Count);

        foreach (var cert in due)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            // Si ya expiró antes de poder renovar, marcamos y publicamos evento de expiración.
            var decision = CertRenewalRules.Decide(cert.Status, cert.RenewAfter, cert.NotAfter, now);
            if (decision == CertRenewalDecision.Skip)
            {
                continue;
            }

            if (decision == CertRenewalDecision.Expire)
            {
                logger.LogWarning("Certificate {CertId} ({Host}) expiró: emitiendo CertificateExpiredEvent",
                    cert.Id, cert.Hostname.Value);

                cert.MarkExpired();
                await outbox.EnqueueAsync(
                    new CertificateExpiredEvent(cert.Id.ToString(), cert.Hostname.Value, now, cert.LastError),
                    ct).ConfigureAwait(false);

                // Se programa el siguiente intento antes de seguir: el certificado ya caduco, pero
                // eso es justo cuando mas urge renovarlo. Marcarlo y olvidarlo dejaria el host sin
                // TLS de forma permanente. El evento ya no se repetira porque el estado cambio.
                cert.ScheduleRenewalRetry(CertRenewalRules.NextRetryAfter(now, BackoffInterval));
                await store.SaveChangesAsync(ct).ConfigureAwait(false);
                continue;
            }

            var result = await manager.RenewAsync(cert.Id, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                logger.LogWarning("Renovación fallida para {CertId} ({Host}): {Error}",
                    cert.Id, cert.Hostname.Value, result.Error);

                if (cert.Status == CertificateStatus.Failed)
                {
                    cert.ScheduleRenewalRetry(CertRenewalRules.NextRetryAfter(now, BackoffInterval));
                    await store.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }
        }
    }
}
