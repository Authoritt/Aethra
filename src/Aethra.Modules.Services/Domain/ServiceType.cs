namespace Aethra.Modules.Services.Domain;

/// <summary>
/// Tipo de servicio gestionado. Cada tipo "data service" (Postgres/Redis/Rabbit/MySQL/MariaDB/Mongo)
/// tiene su <c>IServiceProvisioner</c> asociado que sabe cómo aprovisionar credentials cuando se
/// crea un binding. Los tipos "Application" (WordPress, Ghost, n8n, etc.) y los stores sin
/// provisioner aún (ClickHouse, MeiliSearch, MinIO, PocketBase) se quedan en <c>Application</c>:
/// el catálogo los muestra y el orchestrator los crea, pero no soportan bindings sub-tenant.
/// </summary>
public enum ServiceType
{
    Postgres,
    Redis,
    RabbitMQ,
    MySQL,
    MongoDB,
    MariaDB,
    ClickHouse,
    Application,
}

public static class ServiceTypeExtensions
{
    public static int DefaultInternalPort(this ServiceType type) => type switch
    {
        ServiceType.Postgres => 5432,
        ServiceType.Redis => 6379,
        ServiceType.RabbitMQ => 5672,
        ServiceType.MySQL => 3306,
        ServiceType.MongoDB => 27017,
        ServiceType.MariaDB => 3306,
        ServiceType.ClickHouse => 9000,
        // Application no tiene puerto canónico: cada template lo declara explícito.
        ServiceType.Application => 80,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
