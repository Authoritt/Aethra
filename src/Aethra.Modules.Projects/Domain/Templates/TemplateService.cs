namespace Aethra.Modules.Projects.Domain.Templates;

/// <summary>
/// F13 — un servicio de un template multi-contenedor (ej. Acme: backend + frontend).
/// Se despliega como un contenedor por servicio en una Instance; el orquestador nativo corre
/// <c>{instanceSlug}-{Name}</c> con la <see cref="Image"/> prebuilt y publica rutas según
/// <see cref="PathPrefixes"/> (vacío = servicio interno, sin ruta pública).
///
/// En <see cref="Env"/> el token <c>{instance}</c> se interpola al slug de la Instance al
/// desplegar — permite cablear URLs entre servicios (ej. <c>API_BASE_URL=http://{instance}-backend:5006</c>).
/// </summary>
public sealed record TemplateService(
    string Name,
    string Image,
    int Port,
    IReadOnlyList<string> PathPrefixes,
    IReadOnlyList<KeyValuePair<string, string>> Env,
    // F13.1 — modo de build por servicio: "registry" (pull de Image prebuilt, modelo B) o
    // "git" (Aethra clona y construye DockerfilePath en el satélite, modelo A). Default registry
    // para compat con services existentes (jsonb sin el campo deserializa a este valor).
    string BuildMode = "registry",
    // Solo modo "git": ruta al Dockerfile del servicio dentro del repo (default "Dockerfile").
    string? DockerfilePath = null,
    // F13.3 — volúmenes persistentes del servicio (ej. DataProtection keys). Null = sin volúmenes
    // (jsonb sin el campo deserializa a este valor). Se montan al desplegar cada Instance.
    IReadOnlyList<ServiceVolume>? Volumes = null,
    // Hostname público propio del servicio para apps multi-host (ej. una app con api/admin/tenant
    // cada uno en su dominio). null = usa el hostname de la Instance (customDomain/autoHostname).
    // Las rutas de este servicio (por PathPrefix) se publican bajo este host. jsonb sin campo = null.
    string? Hostname = null);

/// <summary>
/// F13.3 — un volumen persistente montado en un servicio del deploy nativo. El token
/// <c>{instance}</c> en <see cref="Name"/> se interpola al slug de la Instance al desplegar, de
/// modo que cada Instance del template tiene su propio named volume (ej. <c>{instance}-dpkeys</c>).
/// </summary>
public sealed record ServiceVolume(
    string Name,
    string ContainerPath,
    bool ReadOnly = false);
