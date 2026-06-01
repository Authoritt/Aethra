namespace Aethra.Modules.Settings.Domain;

/// <summary>
/// Tipo del proveedor externo al que pertenece la credencial. Solo es metadata: el
/// resolver no usa el tipo para nada (las credenciales se buscan por <c>Name</c>),
/// pero la UI lo aprovecha para mostrar iconos y agrupar por proveedor.
/// </summary>
public enum IntegrationCredentialType
{
    /// <summary>Token API de Cloudflare (acceso a Zones/DNS/Tunnel).</summary>
    Cloudflare = 0,

    /// <summary>Personal Access Token de GitHub (clone privado, webhooks).</summary>
    GitHubPat = 1,

    /// <summary>Credencial SMTP (usuario+password o app password).</summary>
    Smtp = 2,

    /// <summary>Credencial de registry Docker (interno o externo).</summary>
    Registry = 3,

    /// <summary>Cualquier otro proveedor que solo necesite un API key opaco.</summary>
    GenericApiKey = 4,
}
