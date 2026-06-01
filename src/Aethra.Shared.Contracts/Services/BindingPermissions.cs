namespace Aethra.Shared.Contracts.Services;

/// <summary>
/// Permisos de un <c>ServiceBinding</c> sobre el recurso (BD/vhost/keyspace).
///
/// Vive en <c>Shared.Contracts</c> para que módulos externos (Mcp) puedan construir
/// el <c>CreateBindingCommand</c> sin depender de <c>Aethra.Modules.Services.Domain</c>.
/// </summary>
public enum BindingPermissions
{
    Owner,        // Postgres: OWNER; RabbitMQ: configure+write+read
    ReadWrite,    // Postgres: SELECT/INSERT/UPDATE/DELETE; RabbitMQ: write+read
    ReadOnly,     // Postgres: SELECT; RabbitMQ: read
}
