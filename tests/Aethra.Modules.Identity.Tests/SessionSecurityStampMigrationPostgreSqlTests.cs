using Aethra.Modules.Identity.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class SessionSecurityStampMigrationPostgreSqlTests(PostgreSql18Fixture postgres)
    : IClassFixture<PostgreSql18Fixture>
{
    [Fact]
    public async Task Migration_Gh82_populates_distinct_non_empty_stamps_for_existing_users()
    {
        // 1. Crear base de datos aislada en el contenedor PostgreSQL
        var dbName = $"sec_stamp_mig_{Guid.NewGuid():N}";
        await using (var masterConn = new NpgsqlConnection(postgres.ConnectionString))
        {
            await masterConn.OpenAsync();
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        var connStr = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = dbName,
        }.ConnectionString;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Aethra"] = connStr,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentityModule(config);
        await using var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<IdentityDbContext>();
        var migrator = db.Database.GetService<IMigrator>();

        // 2. Aplicar migraciones hasta la anterior a Gh82 (la tabla users existe pero SIN columna security_stamp)
        await migrator.MigrateAsync("20261018172025_Gh85AuthenticationRateLimits");

        // 3. Insertar dos usuarios existentes directamente vía SQL antes de la migración del stamp
        await using (var conn = new NpgsqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO identity.users (id, email, password_hash_cipher, is_active, created_at, updated_at)
                VALUES 
                    ('usr_pre_existing_1', 'user1@example.local', E'\\x010203', true, now(), now()),
                    ('usr_pre_existing_2', 'user2@example.local', E'\\x040506', true, now(), now());
                """;
            await insertCmd.ExecuteNonQueryAsync();
        }

        // 4. Aplicar la migración Gh82 (y cualquier posterior si hubiera)
        await db.Database.MigrateAsync();

        // 5. Verificar que las filas existentes tienen security_stamp no nulo, no vacío y distintos entre sí
        var stamps = new List<string>();
        await using (var conn = new NpgsqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var queryCmd = conn.CreateCommand();
            queryCmd.CommandText = "SELECT id, security_stamp FROM identity.users ORDER BY id;";
            await using var reader = await queryCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                var stamp = reader.GetString(1);
                stamp.Should().NotBeNullOrWhiteSpace($"el usuario {id} debe tener security_stamp no vacío");
                stamps.Add(stamp);
            }
        }

        stamps.Should().HaveCount(2);
        stamps[0].Should().NotBe(stamps[1], "filas existentes deben recibir stamps aleatorios y distintos");
    }
}
