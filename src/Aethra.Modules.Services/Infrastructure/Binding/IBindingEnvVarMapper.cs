using System.Globalization;
using Aethra.Modules.Services.Domain;
using Aethra.Modules.Services.Infrastructure.Provisioning;
using Aethra.Shared.Contracts.Projects;

namespace Aethra.Modules.Services.Infrastructure.Binding;

/// <summary>
/// Construye la lista de env vars a inyectar en una Instance cuando se crea/rota un binding.
/// Cada <see cref="ServiceType"/> tiene su propio conjunto canónico (DATABASE_URL, REDIS_URL, etc.).
/// El prefix opcional permite que una misma instance bindée dos servicios del mismo tipo
/// (ej. <c>ORDERS_DATABASE_URL</c> y <c>BILLING_DATABASE_URL</c>).
/// </summary>
/// <remarks>
/// F9.0 cleanup: el flag <c>IsSecret</c> desapareció del contrato <see cref="EnvVarUpsert"/>;
/// F9.1 reintroducirá la separación enviando passwords vía <c>ISecretWriter</c> (tabla cifrada
/// aparte). Mientras tanto, los URL completos con credenciales viajan como env vars normales —
/// aceptable durante el refactor porque toda la infraestructura sigue en localhost dev.
/// </remarks>
public interface IBindingEnvVarMapper
{
    IReadOnlyList<EnvVarUpsert> Build(ManagedService service, ServiceBinding binding, BindingCredentials creds);
}

public sealed class DefaultBindingEnvVarMapper : IBindingEnvVarMapper
{
    public IReadOnlyList<EnvVarUpsert> Build(ManagedService service, ServiceBinding binding, BindingCredentials creds)
    {
        var prefix = binding.InjectedEnvVarPrefix ?? string.Empty;
        var host = service.ContainerName;
        var port = service.InternalPort;

        return service.Type switch
        {
            ServiceType.Postgres => BuildPostgres(prefix, host, port, binding.ResourceName, creds),
            ServiceType.Redis => BuildRedis(prefix, host, port, binding.ResourceName, creds),
            ServiceType.RabbitMQ => BuildRabbitMQ(prefix, host, port, binding.ResourceName, creds),
            ServiceType.MySQL => BuildMySQL(prefix, host, port, binding.ResourceName, creds),
            ServiceType.MongoDB => BuildMongoDB(prefix, host, port, binding.ResourceName, creds),
            _ => [],
        };
    }

    private static List<EnvVarUpsert> BuildPostgres(string prefix, string host, int port, string db, BindingCredentials creds) =>
    [
        Var(prefix, "DATABASE_URL", $"Host={host};Port={port};Database={db};Username={creds.Username};Password={creds.Password};"),
        Var(prefix, "POSTGRES_HOST", host),
        Var(prefix, "POSTGRES_PORT", port.ToString(CultureInfo.InvariantCulture)),
        Var(prefix, "POSTGRES_DB", db),
        Var(prefix, "POSTGRES_USER", creds.Username),
        Var(prefix, "POSTGRES_PASSWORD", creds.Password),
    ];

    private static List<EnvVarUpsert> BuildRedis(string prefix, string host, int port, string keyspace, BindingCredentials creds) =>
    [
        Var(prefix, "REDIS_URL", $"redis://{creds.Username}:{creds.Password}@{host}:{port}/0"),
        Var(prefix, "REDIS_HOST", host),
        Var(prefix, "REDIS_PORT", port.ToString(CultureInfo.InvariantCulture)),
        Var(prefix, "REDIS_USERNAME", creds.Username),
        Var(prefix, "REDIS_PASSWORD", creds.Password),
        Var(prefix, "REDIS_PREFIX", $"{keyspace}:"),
    ];

    private static List<EnvVarUpsert> BuildRabbitMQ(string prefix, string host, int port, string vhost, BindingCredentials creds) =>
    [
        Var(prefix, "RABBITMQ_URL", $"amqp://{creds.Username}:{creds.Password}@{host}:{port}/{vhost}"),
        Var(prefix, "RABBITMQ_HOST", host),
        Var(prefix, "RABBITMQ_PORT", port.ToString(CultureInfo.InvariantCulture)),
        Var(prefix, "RABBITMQ_VHOST", vhost),
        Var(prefix, "RABBITMQ_USER", creds.Username),
        Var(prefix, "RABBITMQ_PASSWORD", creds.Password),
    ];

    private static List<EnvVarUpsert> BuildMySQL(string prefix, string host, int port, string db, BindingCredentials creds) =>
    [
        Var(prefix, "MYSQL_URL", $"Server={host};Port={port};Database={db};Uid={creds.Username};Pwd={creds.Password};"),
        Var(prefix, "MYSQL_HOST", host),
        Var(prefix, "MYSQL_PORT", port.ToString(CultureInfo.InvariantCulture)),
        Var(prefix, "MYSQL_DB", db),
        Var(prefix, "MYSQL_USER", creds.Username),
        Var(prefix, "MYSQL_PASSWORD", creds.Password),
    ];

    private static List<EnvVarUpsert> BuildMongoDB(string prefix, string host, int port, string db, BindingCredentials creds) =>
    [
        Var(prefix, "MONGODB_URL", $"mongodb://{creds.Username}:{creds.Password}@{host}:{port}/{db}"),
        Var(prefix, "MONGODB_HOST", host),
        Var(prefix, "MONGODB_PORT", port.ToString(CultureInfo.InvariantCulture)),
        Var(prefix, "MONGODB_DB", db),
        Var(prefix, "MONGODB_USER", creds.Username),
        Var(prefix, "MONGODB_PASSWORD", creds.Password),
    ];

    private static EnvVarUpsert Var(string prefix, string key, string value)
        => new($"{prefix}{key}", value, IsBuildTime: false, IsRuntime: true);
}
