namespace Aethra.Modules.Services.Domain;

/// <summary>
/// Permisos del binding sobre el recurso (BD/vhost/keyspace).
/// </summary>
public enum BindingPermissions
{
    Owner,        // Postgres: OWNER; RabbitMQ: configure+write+read
    ReadWrite,    // Postgres: SELECT/INSERT/UPDATE/DELETE; RabbitMQ: write+read
    ReadOnly,     // Postgres: SELECT; RabbitMQ: read
}
