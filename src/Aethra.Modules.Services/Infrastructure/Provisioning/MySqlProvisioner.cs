using System.Text.RegularExpressions;
using Aethra.Modules.Services.Domain;
using Aethra.Shared.Contracts.Services;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Aethra.Modules.Services.Infrastructure.Provisioning;

/// <summary>
/// Aprovisiona BDs/usuarios sobre una instancia MySQL existente.
/// MariaDB hereda esta clase (wire protocol idéntico) — ver <see cref="MariaDbProvisioner"/>.
///
/// MySQL no permite parámetros ligados para identificadores; usamos un patrón restrictivo
/// (mismo alfabeto que Postgres) y luego encerramos en backticks. Cualquier carácter fuera
/// del set produce <c>mysql.invalid_identifier</c>.
/// </summary>
public partial class MySqlProvisioner : IServiceProvisioner
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifierPattern();

    // mysql error codes: https://mariadb.com/kb/en/mariadb-error-codes/
    private const int ErrDbCreateExists = 1007;     // DB ya existe
    private const int ErrCannotUserExists = 1396;   // CREATE USER falla porque ya existe
    private const string AdminDatabase = "mysql";

    private readonly IManagedServiceHostResolver _hostResolver;
    private readonly IAdminCredentialsCodec _codec;
    private readonly ILogger _logger;

    public MySqlProvisioner(
        IManagedServiceHostResolver hostResolver,
        IAdminCredentialsCodec codec,
        ILogger<MySqlProvisioner> logger)
    {
        _hostResolver = hostResolver;
        _codec = codec;
        _logger = logger;
    }

    /// <summary>Constructor protegido para que MariaDbProvisioner reutilice la lógica.</summary>
    protected MySqlProvisioner(
        IManagedServiceHostResolver hostResolver,
        IAdminCredentialsCodec codec,
        ILogger logger)
    {
        _hostResolver = hostResolver;
        _codec = codec;
        _logger = logger;
    }

    public virtual ServiceType SupportedType => ServiceType.MySQL;

    public async Task<ProvisionOutcome> ProvisionAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        if (!SafeIdentifierPattern().IsMatch(binding.ResourceName) ||
            !SafeIdentifierPattern().IsMatch(newCreds.Username))
        {
            return new ProvisionOutcome(false, "mysql.invalid_identifier",
                "ResourceName/Username fuera del alfabeto permitido (alphanum + underscore, <=63).");
        }

        var db = Quote(binding.ResourceName);
        var user = QuoteValue(newCreds.Username);
        var pwd = QuoteValue(newCreds.Password);

        try
        {
            await using var conn = await OpenAdminAsync(service, cancellationToken).ConfigureAwait(false);

            await ExecuteIgnoreCodeAsync(conn,
                $"CREATE DATABASE {db} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
                ErrDbCreateExists, cancellationToken).ConfigureAwait(false);

            await ExecuteIgnoreCodeAsync(conn,
                $"CREATE USER {user}@'%' IDENTIFIED BY {pwd}",
                ErrCannotUserExists, cancellationToken).ConfigureAwait(false);

            // Si el user ya existía con otro password, lo alineamos: ALTER es idempotente.
            await ExecuteAsync(conn,
                $"ALTER USER {user}@'%' IDENTIFIED BY {pwd}", cancellationToken).ConfigureAwait(false);

            var grant = binding.Permissions switch
            {
                BindingPermissions.ReadOnly => "SELECT",
                BindingPermissions.ReadWrite => "SELECT, INSERT, UPDATE, DELETE",
                _ => "ALL PRIVILEGES",
            };
            await ExecuteAsync(conn,
                $"GRANT {grant} ON {db}.* TO {user}@'%'", cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(conn, "FLUSH PRIVILEGES", cancellationToken).ConfigureAwait(false);
            return new ProvisionOutcome(true, null, null);
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "MySQL provision falló binding {Binding}: {Number}", binding.Id, ex.Number);
            return new ProvisionOutcome(false, $"mysql.{ex.Number}", ex.Message);
        }
    }

    public async Task<RevokeOutcome> RevokeAsync(ManagedService service, ServiceBinding binding, BindingCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(credentials);

        if (!SafeIdentifierPattern().IsMatch(binding.ResourceName) ||
            !SafeIdentifierPattern().IsMatch(credentials.Username))
        {
            return new RevokeOutcome(false, "mysql.invalid_identifier", "ResourceName/Username fuera del alfabeto permitido.");
        }

        var user = QuoteValue(credentials.Username);
        var db = Quote(binding.ResourceName);

        try
        {
            await using var conn = await OpenAdminAsync(service, cancellationToken).ConfigureAwait(false);
            await TryExecuteAsync(conn, $"REVOKE ALL PRIVILEGES, GRANT OPTION FROM {user}@'%'", cancellationToken).ConfigureAwait(false);
            await TryExecuteAsync(conn, $"DROP USER IF EXISTS {user}@'%'", cancellationToken).ConfigureAwait(false);
            await TryExecuteAsync(conn, $"DROP DATABASE IF EXISTS {db}", cancellationToken).ConfigureAwait(false);
            await TryExecuteAsync(conn, "FLUSH PRIVILEGES", cancellationToken).ConfigureAwait(false);
            return new RevokeOutcome(true, null, null);
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "MySQL revoke falló binding {Binding}: {Number}", binding.Id, ex.Number);
            return new RevokeOutcome(false, $"mysql.{ex.Number}", ex.Message);
        }
    }

    public async Task<RotateOutcome> RotateAsync(ManagedService service, ServiceBinding binding, BindingCredentials newCreds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(newCreds);

        if (!SafeIdentifierPattern().IsMatch(newCreds.Username))
        {
            return new RotateOutcome(false, "mysql.invalid_identifier", "Username fuera del alfabeto permitido.");
        }

        var user = QuoteValue(newCreds.Username);
        var pwd = QuoteValue(newCreds.Password);

        try
        {
            await using var conn = await OpenAdminAsync(service, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(conn, $"ALTER USER {user}@'%' IDENTIFIED BY {pwd}", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(conn, "FLUSH PRIVILEGES", cancellationToken).ConfigureAwait(false);
            return new RotateOutcome(true, null, null);
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "MySQL rotate falló binding {Binding}: {Number}", binding.Id, ex.Number);
            return new RotateOutcome(false, $"mysql.{ex.Number}", ex.Message);
        }
    }

    public async Task<TestOutcome> TestConnectionAsync(ManagedService service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            await using var conn = await OpenAdminAsync(service, cancellationToken).ConfigureAwait(false);
            await using var cmd = new MySqlCommand("SELECT VERSION()", conn);
            var detail = (await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString();
            return new TestOutcome(true, detail);
        }
        catch (MySqlException ex)
        {
            return new TestOutcome(false, ex.Message);
        }
    }

    private async Task<MySqlConnection> OpenAdminAsync(ManagedService service, CancellationToken cancellationToken)
    {
        var admin = _codec.Decode(service.AdminCredentialsCipher);
        var host = await _hostResolver.ResolveAsync(service, cancellationToken).ConfigureAwait(false);

        var csb = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)service.InternalPort,
            UserID = admin.Username,
            Password = admin.Password,
            Database = AdminDatabase,
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 30,
            Pooling = false,
            SslMode = MySqlSslMode.Preferred,
        };

        var conn = new MySqlConnection(csb.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return conn;
    }

    private static async Task ExecuteAsync(MySqlConnection conn, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteIgnoreCodeAsync(MySqlConnection conn, string sql, int codeToIgnore, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(conn, sql, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException ex) when (ex.Number == codeToIgnore)
        {
            // Idempotente: el objeto ya existe.
        }
    }

    private static async Task TryExecuteAsync(MySqlConnection conn, string sql, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(conn, sql, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException)
        {
            // Best-effort para revoke; el caller registra el outcome global.
        }
    }

    // MySQL identifiers van en backticks; reemplazamos cualquier backtick literal (no debería
    // haber porque el regex lo prohíbe, pero defensa en profundidad).
    private static string Quote(string raw) => "`" + raw.Replace("`", "``", StringComparison.Ordinal) + "`";

    // Para literales (passwords, user@'%') comillas simples + escape de comilla.
    private static string QuoteValue(string raw) => "'" + raw.Replace("'", "''", StringComparison.Ordinal).Replace("\\", "\\\\", StringComparison.Ordinal) + "'";
}
