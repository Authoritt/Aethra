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
            var anyFailure = false;
            try
            {
                anyFailure = await RunPassAsync(stoppingToken).ConfigureAwait(false);
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
                anyFailure = true;
            }

            var next = anyFailure ? BackoffInterval : NormalInterval;
            try
            {
                await Task.Delay(next, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> RunPassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICertificateStore>();
        var manager = scope.ServiceProvider.GetRequiredService<ICertManager>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        var now = clock.GetUtcNow();
        var due = await store.ListIssuedDueForRenewalAsync(now, ct).ConfigureAwait(false);
        if (due.Count == 0)
        {
            return false;
        }

        logger.LogInformation("Renovando {Count} certificate(s) en este pase", due.Count);

        var anyFailure = false;
        foreach (var cert in due)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            // Si ya expiró antes de poder renovar, marcamos y publicamos evento de expiración.
            if (cert.NotAfter is { } na && na <= now)
            {
                logger.LogWarning("Certificate {CertId} ({Host}) expiró: emitiendo CertificateExpiredEvent",
                    cert.Id, cert.Hostname.Value);

                await outbox.EnqueueAsync(
                    new CertificateExpiredEvent(cert.Id.ToString(), cert.Hostname.Value, now, cert.LastError),
                    ct).ConfigureAwait(false);
                await store.SaveChangesAsync(ct).ConfigureAwait(false);
                anyFailure = true;
                continue;
            }

            var result = await manager.RenewAsync(cert.Id, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                logger.LogWarning("Renovación fallida para {CertId} ({Host}): {Error}",
                    cert.Id, cert.Hostname.Value, result.Error);
                anyFailure = true;
            }
        }
        return anyFailure;
    }
}
