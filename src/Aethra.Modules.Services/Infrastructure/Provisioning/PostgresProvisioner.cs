using System.Globalization;
using Aethra.Modules.Services.Domain;
using Aethra.Shared.Contracts.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Provisions databases and roles on an existing PostgreSQL instance.
/// </summary>
public sealed class PostgresProvisioner : IServiceProvisioner
{
    private const string AdminDatabase = "postgres";
    private const string DuplicateObject = "42710";
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
            _logger.LogError(ex, "Postgres provision fallo para binding {Binding}: {SqlState}", binding.Id, ex.SqlState);
            return new ProvisionOutcome(false, $"postgres.{ex.SqlState}", ex.MessageText);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex, "Postgres provision fallo de conexion para binding {Binding}", binding.Id);
            return new ProvisionOutcome(false, "postgres.connect_failed", ex.Message);
        }
    }

    public async Task<RevokeOutcome> RevokeAsync(ManagedService service, ServiceBinding binding, BindingCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(credentials);

        PostgresRevokePlan plan;
        AdminCredentials admin;
        try
        {
            admin = _codec.Decode(service.AdminCredentialsCipher);
            plan = PostgresRevokePlan.Create(binding.ResourceName, credentials, admin);
        }
        catch (ArgumentException ex)
        {
            return new RevokeOutcome(false, "postgres.invalid_identifier", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new RevokeOutcome(false, "postgres.admin_credentials_unreadable", ex.Message);
        }

        try
        {
            await using var adminConn = await OpenAdminAsync(service, AdminDatabase, admin, cancellationToken).ConfigureAwait(false);

            var outcome = await ExecuteRevokeStepAsync(adminConn, PostgresRevokeStep.RevokeDatabasePrivileges,
                $"REVOKE ALL PRIVILEGES ON DATABASE {plan.DatabaseIdentifier} FROM {plan.UserIdentifier}", cancellationToken).ConfigureAwait(false);
            if (!outcome.Success) { return outcome; }

            outcome = await ExecuteRevokeStepAsync(adminConn, PostgresRevokeStep.RestoreDatabaseOwner,
                $"ALTER DATABASE {plan.DatabaseIdentifier} OWNER TO {plan.AdminIdentifier}", cancellationToken).ConfigureAwait(false);
            if (!outcome.Success) { return outcome; }

            outcome = await CleanupTargetDatabaseAsync(service, plan.DatabaseName, admin,
                plan.UserIdentifier, plan.AdminIdentifier, cancellationToken).ConfigureAwait(false);
            if (!outcome.Success) { return outcome; }

            outcome = await ExecuteRevokeStepAsync(adminConn, PostgresRevokeStep.DropRole,
                $"DROP ROLE IF EXISTS {plan.UserIdentifier}", cancellationToken).ConfigureAwait(false);
            if (!outcome.Success) { return outcome; }

            if (await RoleExistsAsync(adminConn, plan.Username, cancellationToken).ConfigureAwait(false))
            {
                return new RevokeOutcome(false, "postgres.role_still_exists",
                    $"Postgres role '{plan.Username}' still exists after revoke.");
            }

            return new RevokeOutcome(true, null, null);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Postgres revoke fallo para binding {Binding}: {SqlState}", binding.Id, ex.SqlState);
            return new RevokeOutcome(false, $"postgres.{ex.SqlState}", ex.MessageText);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex, "Postgres revoke fallo de conexion para binding {Binding}", binding.Id);
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
            _logger.LogError(ex, "Postgres rotate fallo binding {Binding}: {SqlState}", binding.Id, ex.SqlState);
            return new RotateOutcome(false, $"postgres.{ex.SqlState}", ex.MessageText);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex, "Postgres rotate fallo de conexion binding {Binding}", binding.Id);
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
        await using var dbConn = await OpenAdminAsync(service, binding.ResourceName, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(dbConn, $"REVOKE ALL ON SCHEMA public FROM {userQ}", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(dbConn, $"GRANT USAGE ON SCHEMA public TO {userQ}", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(dbConn, $"GRANT SELECT ON ALL TABLES IN SCHEMA public TO {userQ}", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(dbConn, $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO {userQ}", cancellationToken).ConfigureAwait(false);
    }

    private async Task<RevokeOutcome> CleanupTargetDatabaseAsync(
        ManagedService service,
        string database,
        AdminCredentials admin,
        string userQ,
        string adminQ,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var dbConn = await OpenAdminAsync(service, database, admin, cancellationToken).ConfigureAwait(false);

            var outcome = await ExecuteRevokeStepAsync(dbConn, PostgresRevokeStep.ReassignOwnedObjects,
                $"REASSIGN OWNED BY {userQ} TO {adminQ}", cancellationToken).ConfigureAwait(false);
            if (!outcome.Success) { return outcome; }

            return await ExecuteRevokeStepAsync(dbConn, PostgresRevokeStep.DropOwnedObjects,
                $"DROP OWNED BY {userQ}", cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (
            PostgresRevokeRules.Classify(PostgresRevokeStep.OpenTargetDatabase, ex.SqlState) ==
            PostgresRevokeErrorDecision.BenignIdempotent)
        {
            return new RevokeOutcome(true, null, null);
        }
    }

    private static async Task<RevokeOutcome> ExecuteRevokeStepAsync(
        NpgsqlConnection conn,
        PostgresRevokeStep step,
        string sql,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(conn, sql, cancellationToken).ConfigureAwait(false);
            return new RevokeOutcome(true, null, null);
        }
        catch (PostgresException ex) when (
            PostgresRevokeRules.Classify(step, ex.SqlState) == PostgresRevokeErrorDecision.BenignIdempotent)
        {
            return new RevokeOutcome(true, null, null);
        }
        catch (PostgresException ex)
        {
            return new RevokeOutcome(false, $"postgres.revoke.{ex.SqlState}",
                $"{step} failed with SQLSTATE {ex.SqlState}: {ex.MessageText}");
        }
    }

    private static async Task<bool> RoleExistsAsync(NpgsqlConnection conn, string username, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @username)", conn);
        cmd.Parameters.AddWithValue("username", username);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
    }

    private async Task<NpgsqlConnection> OpenAdminAsync(ManagedService service, string database, CancellationToken cancellationToken)
    {
        var admin = _codec.Decode(service.AdminCredentialsCipher);
        return await OpenAdminAsync(service, database, admin, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenAdminAsync(ManagedService service, string database, AdminCredentials admin, CancellationToken cancellationToken)
    {
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
            // Idempotent: the resource already exists.
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
