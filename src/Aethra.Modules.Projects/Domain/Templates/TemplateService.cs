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
    IReadOnlyList<KeyValuePair<string, string>> Env);
