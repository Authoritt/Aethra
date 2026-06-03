namespace Aethra.Shared.Contracts.Projects;

/// <summary>
/// F12.3 — Read+write API cross-module para el webhook handler de Deployments. Encapsula la
/// lógica de "PR webhook → Instance ephemeral" sin que <c>Modules.Deployments</c> tenga que
/// referenciar internals de <c>Modules.Projects</c> (Template aggregate, Client lazy-create,
/// EF context). Lo implementa <c>Modules.Projects</c>.
///
/// El handler del webhook lo invoca tras validar HMAC y resolver el User Aethra del autor del PR.
/// </summary>
public interface IPreviewInstanceCoordinator
{
    /// <summary>
    /// Atómico: lazy-create del Client interno <c>__preview__</c>, picks round-robin de VM con
    /// <c>AcceptsPreviews=true</c>, crea Instance ephemeral con TrackedRef=<c>refs/pull/{N}/head</c>,
    /// dispara Build. Idempotente: si ya existe una Instance ephemeral para
    /// (templateId, prNumber), la reutiliza y solo redeploya.
    ///
    /// Retorna info necesaria para postear el comment de GitHub (hostname auto, PR URL, etc.).
    /// </summary>
    Task<PreviewProvisioningResult> EnsurePreviewAsync(
        string templateId,
        int prNumber,
        string headSha,
        string? createdByUserId,
        CancellationToken ct);

    /// <summary>
    /// Borra la Instance ephemeral asociada al PR (si existe). Emite el integration event de
    /// remove para que Proxy/Cloudflare limpien la Route y Containers paren el contenedor.
    /// Idempotente: si la Instance ya no existe, devuelve <see cref="PreviewTeardownResult.NotFound"/>.
    /// </summary>
    Task<PreviewTeardownResult> TeardownPreviewAsync(
        string templateId,
        int prNumber,
        CancellationToken ct);

    /// <summary>
    /// Cuenta cuántas Instances ephemerals tiene el Project (resuelto vía Template).
    /// </summary>
    Task<int> CountActivePreviewsForProjectAsync(string projectId, CancellationToken ct);

    /// <summary>
    /// Lee el cap configurado del Project. Default 10.
    /// </summary>
    Task<int> GetPreviewQuotaAsync(string projectId, CancellationToken ct);
}

public enum PreviewProvisioningStatus
{
    Created,
    Reused,
    QuotaExceeded,
    NoVmAvailable,
    PreviewsDisabled,
    TemplateNotFound,
}

/// <summary>
/// Resultado del provisioning de una preview.
/// </summary>
/// <param name="Status">Outcome de la operación.</param>
/// <param name="InstanceId">ID de la Instance creada o reusada (null en errores).</param>
/// <param name="Hostname">Auto-hostname o custom domain efectivo (null si no hay base domain).</param>
/// <param name="QuotaActual">Solo poblado si Status=QuotaExceeded; cantidad actual.</param>
/// <param name="QuotaMax">Solo poblado si Status=QuotaExceeded; cap configurado.</param>
public sealed record PreviewProvisioningResult(
    PreviewProvisioningStatus Status,
    string? InstanceId,
    string? Hostname,
    int? QuotaActual = null,
    int? QuotaMax = null);

public enum PreviewTeardownStatus
{
    Removed,
    NotFound,
}

public sealed record PreviewTeardownResult(
    PreviewTeardownStatus Status,
    string? InstanceId);
