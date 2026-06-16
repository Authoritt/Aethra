namespace Aethra.Shared.Contracts.Deployments;

/// <summary>
/// F13.1 — un push (webhook) afectó a una Instance multi-servicio (Template con <c>Services</c>)
/// cuyo branch trackeado coincide. El host lo consume y dispara el deploy NATIVO en background
/// (build-from-git/registry por servicio + rutas + monitor). Reemplaza, para apps multi-servicio,
/// al pipeline single-container Build→Deploy.
/// </summary>
public sealed record NativeRedeployRequestedIntegrationEvent(
    string InstanceId,
    string Reason,
    string? ServiceName = null) : IntegrationEvent;
