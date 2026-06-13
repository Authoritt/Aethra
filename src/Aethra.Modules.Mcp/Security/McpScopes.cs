namespace Aethra.Modules.Mcp.Security;

/// <summary>
/// Catálogo de scopes que cada tool MCP requiere. Los valores deben existir en
/// <c>Aethra.Modules.Identity.Domain.ApiKey.AllScopes</c> — si alguno falla,
/// <see cref="McpToolAuthorizer"/> rechazará la tool en runtime.
///
/// El scope wildcard <c>"*"</c> (admin) está implícito: <see cref="McpToolAuthorizer"/>
/// lo acepta para cualquier requirement.
/// </summary>
internal static class McpScopes
{
    public const string ContextRead = "context:read";

    public const string ProjectsRead = "projects:read";
    public const string ProjectsWrite = "projects:write";

    public const string DeploymentsRead = "deployments:read";
    public const string DeploymentsTrigger = "deployments:trigger";

    public const string ServicesRead = "services:read";
    public const string ServicesWrite = "services:write";

    public const string MonitoringRead = "monitoring:read";
    public const string MonitoringWrite = "monitoring:write";

    public const string MetricsRead = "metrics:read";

    public const string CloudflareRead = "cloudflare:read";
    public const string CloudflareWrite = "cloudflare:write";

    public const string VmsRead = "vms:read";
    public const string VmsWrite = "vms:write";

    public const string NotesWrite = "notes:write";

    // F11.5 — scopes para los nuevos features expuestos via MCP.
    public const string UsersRead = "users:read";
    public const string UsersWrite = "users:write";

    public const string NotificationsRead = "notifications:read";
    public const string NotificationsWrite = "notifications:write";
}
