using System.Security.Cryptography.X509Certificates;
using Aethra.Modules.Proxy.Domain;
using Aethra.Shared.Contracts.Proxy;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Proxy.Infrastructure.Tls;

/// <summary>
/// Implementación de <see cref="ICertManager"/> usando Let's Encrypt y la librería Certes.
/// Flujo de emisión:
/// <list type="number">
///   <item>Carga (o crea) la account key persistida en <c>tls_account</c>.</item>
///   <item>Crea un <c>AcmeOrder</c> para el hostname.</item>
///   <item>Recupera el desafío HTTP-01, lo deposita en <see cref="IAcmeChallengeStore"/>.</item>
///   <item>Pide a la CA validar y hace polling con backoff <c>[2,5,10,20]s</c>.</item>
///   <item>Genera una key RSA-2048 nueva, finaliza la orden y descarga el cert.</item>
///   <item>Construye un PFX, lo cifra con DataProtection y lo guarda.</item>
/// </list>
/// </summary>
public sealed class LetsEncryptCertManager : ICertManager
{
    // Purposes de DataProtection: separados para que un compromiso de uno no exponga el otro.
    private const string PfxPurpose = "aethra-cert-pfx";
    private const string AccountKeyPurpose = "aethra-acme-account";

    // Password "interna" del PFX. No es secreto real: el PFX completo se cifra con DataProtection
    // antes de tocar disco/BD. La password solo es requerida por la API del .NET para encapsular
    // la clave privada dentro del contenedor PFX.
    private const string PfxPassword = "aethra";

    private static readonly TimeSpan[] PollingDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
    ];

    private readonly ICertificateStore _store;
    private readonly IAcmeChallengeStore _challenges;
    private readonly IOutboxWriter _outbox;
    private readonly IDataProtector _pfxProtector;
    private readonly IDataProtector _accountProtector;
    private readonly TlsOptions _options;
    private readonly ILogger<LetsEncryptCertManager> _logger;
    private readonly TimeProvider _clock;

    public LetsEncryptCertManager(
        ICertificateStore store,
        IAcmeChallengeStore challenges,
        IOutboxWriter outbox,
        IDataProtectionProvider dataProtection,
        IOptions<TlsOptions> options,
        ILogger<LetsEncryptCertManager> logger,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(dataProtection);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _challenges = challenges;
        _outbox = outbox;
        _pfxProtector = dataProtection.CreateProtector(PfxPurpose);
        _accountProtector = dataProtection.CreateProtector(AccountKeyPurpose);
        _options = options.Value;
        _logger = logger;
        _clock = clock;

        if (string.IsNullOrWhiteSpace(_options.AccountEmail))
        {
            throw new InvalidOperationException("Tls:AccountEmail es obligatorio.");
        }
        if (_options.RenewBeforeDays <= 0)
        {
            throw new InvalidOperationException("Tls:RenewBeforeDays debe ser > 0.");
        }
    }

    public async Task<Result<Certificate>> RequestAsync(Hostname hostname, CancellationToken ct)
    {
        // Si ya existe un cert para este hostname y está emitido, lo tratamos como renovación.
        // Si está Pending o Failed, reintentamos la emisión sobre el agregado existente.
        var existing = await _store.FindByHostnameAsync(hostname, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Status == CertificateStatus.Issued)
            {
                _logger.LogInformation("Cert {CertId} ya emitido para {Host}; relanzando como renovación.",
                    existing.Id, hostname.Value);
                return await RenewInternalAsync(existing, ct).ConfigureAwait(false);
            }
            _logger.LogInformation("Cert {CertId} en estado {Status} para {Host}; reintentando emisión.",
                existing.Id, existing.Status, hostname.Value);
            return await IssueAsync(existing, isRenewal: false, ct).ConfigureAwait(false);
        }

        var cert = Certificate.Request(hostname);
        await _store.AddAsync(cert, ct).ConfigureAwait(false);
        // Save inicial para que Pending sea visible en BD aunque luego falle ACME.
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        return await IssueAsync(cert, isRenewal: false, ct).ConfigureAwait(false);
    }

    public async Task<Result<Certificate>> RenewAsync(CertificateId id, CancellationToken ct)
    {
        var cert = await _store.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (cert is null)
        {
            return Error.NotFound("certificate.not_found", $"Certificate {id} no existe.");
        }
        return await RenewInternalAsync(cert, ct).ConfigureAwait(false);
    }

    private async Task<Result<Certificate>> RenewInternalAsync(Certificate cert, CancellationToken ct)
    {
        try
        {
            cert.MarkRenewing();
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return Error.Conflict("certificate.invalid_state", ex.Message);
        }
        return await IssueAsync(cert, isRenewal: true, ct).ConfigureAwait(false);
    }

    public async Task<X509Certificate2?> LoadCertAsync(CertificateId id, CancellationToken ct)
    {
        var cert = await _store.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (cert is null || string.IsNullOrEmpty(cert.PfxCipherText) || cert.Status != CertificateStatus.Issued)
        {
            return null;
        }

        var pfxBytes = _pfxProtector.Unprotect(Convert.FromBase64String(cert.PfxCipherText));
        return X509CertificateLoader.LoadPkcs12(pfxBytes, PfxPassword);
    }

    private async Task<Result<Certificate>> IssueAsync(Certificate cert, bool isRenewal, CancellationToken ct)
    {
        try
        {
            var acmeCtx = await GetOrCreateAccountAsync(ct).ConfigureAwait(false);
            var order = await acmeCtx.NewOrder([cert.Hostname.Value]).ConfigureAwait(false);

            var authorizations = await order.Authorizations().ConfigureAwait(false);
            foreach (var authz in authorizations)
            {
                var httpChallenge = await authz.Http().ConfigureAwait(false)
                    ?? throw new InvalidOperationException("La CA no ofreció desafío HTTP-01.");

                var keyAuthz = httpChallenge.KeyAuthz;
                _challenges.Set(httpChallenge.Token, keyAuthz);

                try
                {
                    await httpChallenge.Validate().ConfigureAwait(false);
                    await WaitForAuthorizationAsync(authz, ct).ConfigureAwait(false);
                }
                finally
                {
                    _challenges.Remove(httpChallenge.Token);
                }
            }

            // Clave del certificado nueva en cada emisión/renovación.
            var certKey = KeyFactory.NewKey(KeyAlgorithm.RS256);
            await order.Finalize(new CsrInfo { CommonName = cert.Hostname.Value }, certKey).ConfigureAwait(false);
            await WaitForOrderValidAsync(order, ct).ConfigureAwait(false);

            var chain = await order.Download().ConfigureAwait(false);
            var pfxBuilder = chain.ToPfx(certKey);
            var pfxBytes = pfxBuilder.Build(cert.Hostname.Value, PfxPassword);

            // Extraer NotBefore/NotAfter del cert leaf para alimentar el agregado.
            using var leaf = X509CertificateLoader.LoadPkcs12(pfxBytes, PfxPassword);
            var notBefore = new DateTimeOffset(leaf.NotBefore.ToUniversalTime(), TimeSpan.Zero);
            var notAfter = new DateTimeOffset(leaf.NotAfter.ToUniversalTime(), TimeSpan.Zero);

            var cipher = Convert.ToBase64String(_pfxProtector.Protect(pfxBytes));
            var now = _clock.GetUtcNow();
            cert.MarkIssued(cipher, notBefore, notAfter, _options.RenewBeforeDays, now);

            // Encolar evento de integración en outbox (misma transacción que el SaveChanges).
            if (isRenewal)
            {
                await _outbox.EnqueueAsync(
                    new CertificateRenewedEvent(cert.Id.ToString(), cert.Hostname.Value, notBefore, notAfter),
                    ct).ConfigureAwait(false);
            }
            else
            {
                await _outbox.EnqueueAsync(
                    new CertificateIssuedEvent(cert.Id.ToString(), cert.Hostname.Value, notBefore, notAfter),
                    ct).ConfigureAwait(false);
            }

            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Certificate {CertId} {Action} para {Host}, expira {NotAfter:o}",
                cert.Id, isRenewal ? "renovado" : "emitido", cert.Hostname.Value, notAfter);

            return cert;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló emisión/renovación ACME para {Host}", cert.Hostname.Value);
            cert.MarkFailed(ex.Message);
            try
            {
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Además falló persistir el estado Failed del cert {CertId}", cert.Id);
            }
            return Error.Failure("certificate.acme_failed", ex.Message);
        }
    }

    private async Task WaitForAuthorizationAsync(IAuthorizationContext authz, CancellationToken ct)
    {
        foreach (var delay in PollingDelays)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            var res = await authz.Resource().ConfigureAwait(false);
            if (res.Status == AuthorizationStatus.Valid)
            {
                return;
            }
            if (res.Status == AuthorizationStatus.Invalid)
            {
                throw new InvalidOperationException($"Authorization invalid: {res.Challenges?.FirstOrDefault()?.Error?.Detail}");
            }
        }
        throw new TimeoutException("ACME authorization no alcanzó estado Valid tras los polls configurados.");
    }

    private async Task WaitForOrderValidAsync(IOrderContext order, CancellationToken ct)
    {
        foreach (var delay in PollingDelays)
        {
            var res = await order.Resource().ConfigureAwait(false);
            if (res.Status == OrderStatus.Valid)
            {
                return;
            }
            if (res.Status == OrderStatus.Invalid)
            {
                throw new InvalidOperationException("ACME order Invalid tras Finalize.");
            }
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("ACME order no llegó a Valid tras Finalize.");
    }

    private async Task<IAcmeContext> GetOrCreateAccountAsync(CancellationToken ct)
    {
        var directoryUri = _options.UseStaging
            ? WellKnownServers.LetsEncryptStagingV2
            : WellKnownServers.LetsEncryptV2;

        var account = await _store.FindAccountAsync(ct).ConfigureAwait(false);
        if (account is not null)
        {
            var pemBytes = _accountProtector.Unprotect(Convert.FromBase64String(account.AccountKeyPemCipherText));
            var pem = System.Text.Encoding.UTF8.GetString(pemBytes);
            var key = KeyFactory.FromPem(pem);
            return new AcmeContext(directoryUri, key);
        }

        // Primera vez: creamos cuenta, generamos key, registramos y la persistimos cifrada.
        var ctx = new AcmeContext(directoryUri);
        _ = await ctx.NewAccount(_options.AccountEmail, termsOfServiceAgreed: true).ConfigureAwait(false);
        var newKeyPem = ctx.AccountKey.ToPem();
        var cipher = Convert.ToBase64String(_accountProtector.Protect(System.Text.Encoding.UTF8.GetBytes(newKeyPem)));

        var entity = AcmeAccount.Create(cipher, _options.AccountEmail, _options.UseStaging, _clock.GetUtcNow());
        await _store.AddAccountAsync(entity, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        return ctx;
    }
}
