using System.Globalization;
using Aethra.Modules.Services.Domain;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Aprovisiona BDs/usuarios sobre una instancia Postgres existente. Conecta con las
/// credenciales admin descifradas y emite DDL contra <c>postgres</c> primero, luego
/// contra la BD del binding cuando necesita GRANT a nivel schema.
/// </summary>
public sealed class PostgresProvisioner : IServiceProvisioner
{
    private const string AdminDatabase = "postgres";
    private const string DuplicateObject = "42710";       // role ya existe
    private const string DuplicateDatabase = "42P04";

    private readonly IManagedServiceHostResolver _hostResolver;
    private readonly IAdminCredentialsCodec _codec;
    private readonly ILogger<PostgresProvisioner> _logger;

    public PostgresProvisioner(
        IManagedServiceHostResolver hostResolver,
        IAdminCredentialsCodec codec,
        ILogger<PostgresProvisioner> logger)
    {
        _hostResolver = hostResolver;
        _codec = codec;
        _logger = logger;
    }

    public ServiceType SupportedType => ServiceType.Postgres;

    public async Task<ProvisionOutcome> ProvisionAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        string dbQ, userQ;
        try
        {
            dbQ = PostgresIdentifier.Quote(binding.ResourceName);
            userQ = PostgresIdentifier.Quote(newCreds.Username);
        }
        catch (ArgumentException ex)
        {
            return new ProvisionOutcome(false, "postgres.invalid_identifier", ex.Message);
        }

        try
        {
            await using var adminConn = await OpenAdminAsync(service, AdminDatabase, cancellationToken).ConfigureAwait(false);

            await ExecuteIgnoreCodeAsync(adminConn, $"CREATE DATABASE {dbQ}", DuplicateDatabase, cancellationToken).ConfigureAwait(false);
            await ExecuteIgnoreCodeAsync(adminConn,
                $"CREATE USER {userQ} WITH PASSWORD '{EscapeLiteral(newCreds.Password)}'",
                DuplicateObject, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(adminConn, $"GRANT ALL PRIVILEGES ON DATABASE {dbQ} TO {userQ}", cancellationToken).ConfigureAwait(false);

            if (TryGetMajorVersion(service.Version, out var major) && major >= 15)
            {
                await ExecuteAsync(adminConn, $"ALTER DATABASE {dbQ} OWNER TO {userQ}", cancellationToken).ConfigureAwait(false);
            }

            if (binding.Permissions == BindingPermissions.ReadOnly)
            {
                await ApplyReadOnlyAsync(service, binding, userQ, cancellationToken).ConfigureAwait(false);
            }

            return new ProvisionOutcome(true, null, null);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Postgres provision falló para binding {Binding}: {SqlState}", binding.Id, ex.SqlState);
            return new ProvisionOutcome(false, $"postgres.{ex.SqlState}", ex.MessageText);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex, "Postgres provision falló (conexión) para binding {Binding}", binding.Id);
            return new ProvisionOutcome(false, "postgres.connect_failed", ex.Message);
        }
    }

    public async Task<RevokeOutcome> RevokeAsync(ManagedService service, ServiceBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);

        string dbQ, userQ;
        try
        {
            dbQ = PostgresIdentifier.Quote(binding.ResourceName);
            // Username está cifrado: lo recibimos como parte del binding aún no, asi que reconstruimos
            // a partir del role buscado vía SQL. Para MVP usamos resourceName como role name si están
            // alineados; el caller debe pasar el username vía rotate antes. Cuando username difiere,
            // este Revoke aún hace REVOKE/DROP idempotente sobre los objetos owned por la BD.
            userQ = PostgresIdentifier.Quote(binding.ResourceName);
        }
        catch (ArgumentException ex)
        {
            return new RevokeOutcome(false, "postgres.invalid_identifier", ex.Message);
        }

        try
        {
            await using var adminConn = await OpenAdminAsync(service, AdminDatabase, cancellationToken).ConfigureAwait(false);

            // REVOKE sobre la BD; si el role ya no existe el comando falla, lo ignoramos.
            await TryExecuteAsync(adminConn, $"REVOKE ALL PRIVILEGES ON DATABASE {dbQ} FROM {userQ}", cancellationToken).ConfigureAwait(false);
            await TryExecuteAsync(adminConn, $"DROP OWNED BY {userQ}", cancellationToken).ConfigureAwait(false);
            await TryExecuteAsync(adminConn, $"DROP USER IF EXISTS {userQ}", cancellationToken).ConfigureAwait(false);

            return new RevokeOutcome(true, null, null);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex, "Postgres revoke falló para binding {Binding}", binding.Id);
            return new RevokeOutcome(false, "postgres.revoke_failed", ex.Message);
        }
    }

    public async Task<RotateOutcome> RotateAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        string userQ;
        try
        {
            userQ = PostgresIdentifier.Quote(newCreds.Username);
        }
        catch (ArgumentException ex)
        {
            return new RotateOutcome(false, "postgres.invalid_identifier", ex.Message);
        }

        try
        {
            await using var adminConn = await OpenAdminAsync(service, AdminDatabase, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(adminConn,
                $"ALTER USER {userQ} WITH PASSWORD '{EscapeLiteral(newCreds.Password)}'",
                cancellationToken).ConfigureAwait(false);
            return new RotateOutcome(true, null, null);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Postgres rotate falló binding {Binding}: {SqlState}", binding.Id, ex.SqlState);
            return new RotateOutcome(false, $"postgres.{ex.SqlState}", ex.MessageText);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex, "Postgres rotate falló (conexión) binding {Binding}", binding.Id);
            return new RotateOutcome(false, "postgres.connect_failed", ex.Message);
        }
    }

    public async Task<TestOutcome> TestConnectionAsync(ManagedService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            await using var conn = await OpenAdminAsync(service, AdminDatabase, cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT version()", conn);
            var detail = (await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString();
            return new TestOutcome(true, detail);
        }
        catch (NpgsqlException ex)
        {
            return new TestOutcome(false, ex.Message);
        }
    }

    private async Task ApplyReadOnlyAsync(ManagedService service, ServiceBinding binding, string userQ, CancellationToken cancellationToken)
    {
        // ReadOnly: el GRANT ALL anterior dio CONNECT y CREATE temporal a nivel de BD; aquí
        // recortamos: revocamos default y damos solo USAGE+SELECT en schema public.
        await using var dbConn = await OpenAdminAsync(service, binding.ResourceName, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(dbConn, $"REVOKE ALL ON SCHEMA public FROM {userQ}", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(dbConn, $"GRANT USAGE ON SCHEMA public TO {userQ}", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(dbConn, $"GRANT SELECT ON ALL TABLES IN SCHEMA public TO {userQ}", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(dbConn, $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO {userQ}", cancellationToken).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenAdminAsync(ManagedService service, string database, CancellationToken cancellationToken)
    {
        var admin = _codec.Decode(service.AdminCredentialsCipher);
        var host = await _hostResolver.ResolveAsync(service, cancellationToken).ConfigureAwait(false);

        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = service.InternalPort,
            Database = database,
            Username = admin.Username,
            Password = admin.Password,
            Timeout = 10,
            CommandTimeout = 30,
            Pooling = false,
        };

        var conn = new NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return conn;
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteIgnoreCodeAsync(NpgsqlConnection conn, string sql, string sqlStateToIgnore, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(conn, sql, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == sqlStateToIgnore)
        {
            // Idempotente: el recurso ya existe.
        }
    }

    private static async Task TryExecuteAsync(NpgsqlConnection conn, string sql, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(conn, sql, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException)
        {
            // Best-effort para revoke; el caller ya hace logging del Outcome.
        }
    }

    private static string EscapeLiteral(string raw) => raw.Replace("'", "''", StringComparison.Ordinal);

    private static bool TryGetMajorVersion(string version, out int major)
    {
        var dot = version.IndexOf('.', StringComparison.Ordinal);
        var head = dot >= 0 ? version[..dot] : version;
        return int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
    }
}
