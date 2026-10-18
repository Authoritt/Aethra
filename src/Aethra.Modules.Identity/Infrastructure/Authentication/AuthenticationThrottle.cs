using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

internal sealed class AuthenticationThrottle
{
    public string PartitionKey { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public DateTimeOffset WindowStartedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}

public readonly record struct AuthenticationAttemptLease(
    bool IsAllowed,
    int AttemptCount,
    DateTimeOffset ExpiresAt);

public interface IAuthenticationAttemptLimiter
{
    ValueTask<AuthenticationAttemptLease> AcquirePasswordAsync(
        string email,
        CancellationToken cancellationToken);

    ValueTask ResetPasswordAsync(string email, CancellationToken cancellationToken);
}

/// <summary>
/// Presupuesto de intentos de contrasena persistido en PostgreSQL. El UPSERT atomico comparte
/// el contador entre replicas y evita ejecutar Argon2 una vez agotado. Los intentos recibidos
/// durante el bloqueo no extienden la ventana fija.
/// </summary>
internal sealed class AuthenticationAttemptLimiter(
    IdentityDbContext db,
    IOptions<IdentityOptions> options,
    IClock clock,
    ILogger<AuthenticationAttemptLimiter> logger) : IAuthenticationAttemptLimiter
{
    private const string AcquireSql = """
        INSERT INTO identity.authentication_throttles
            (partition_key, attempt_count, window_started_at, expires_at, updated_at)
        VALUES
            (@partition_key, 1, @now, @expires_at, @now)
        ON CONFLICT (partition_key) DO UPDATE SET
            attempt_count = CASE
                WHEN identity.authentication_throttles.expires_at <= @now THEN 1
                ELSE identity.authentication_throttles.attempt_count + 1
            END,
            window_started_at = CASE
                WHEN identity.authentication_throttles.expires_at <= @now THEN @now
                ELSE identity.authentication_throttles.window_started_at
            END,
            expires_at = CASE
                WHEN identity.authentication_throttles.expires_at <= @now THEN @expires_at
                ELSE identity.authentication_throttles.expires_at
            END,
            updated_at = @now
        RETURNING attempt_count, expires_at;
        """;

    private readonly AuthenticationRateLimitOptions _options = options.Value.RateLimit;

    public ValueTask<AuthenticationAttemptLease> AcquirePasswordAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return AcquireAsync(
            PartitionKey("password", NormalizeEmail(email)),
            _options.PasswordAttemptLimit,
            now,
            now.AddSeconds(Math.Max(1, _options.PasswordWindowSeconds)),
            cancellationToken);
    }

    public async ValueTask ResetPasswordAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM identity.authentication_throttles
            WHERE partition_key = @partition_key;
            """;

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(Parameter(
                command,
                "partition_key",
                PartitionKey("password", NormalizeEmail(email))));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<AuthenticationAttemptLease> AcquireAsync(
        string partitionKey,
        int configuredLimit,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, configuredLimit);
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = AcquireSql;
            command.Parameters.Add(Parameter(command, "partition_key", partitionKey));
            command.Parameters.Add(Parameter(command, "now", now));
            command.Parameters.Add(Parameter(command, "expires_at", expiresAt));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("No se pudo adquirir el presupuesto de autenticacion.");
            }

            var lease = new AuthenticationAttemptLease(
                reader.GetInt32(0) <= limit,
                reader.GetInt32(0),
                reader.GetFieldValue<DateTimeOffset>(1));
            if (!lease.IsAllowed)
            {
                logger.LogWarning(
                    "Intento de autenticacion rechazado por presupuesto agotado hasta {ExpiresAt}",
                    lease.ExpiresAt);
            }
            return lease;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    internal static string PartitionKey(string kind, string value)
    {
        var bytes = Encoding.UTF8.GetBytes($"{kind}:{value}");
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static DbParameter Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }
}
