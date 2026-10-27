using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Aethra.Modules.Identity.Tests;

public sealed class IdentitySeederPostgreSqlTests(PostgreSql18Fixture postgres)
    : IClassFixture<PostgreSql18Fixture>
{
    private const string AdminEmail = "admin@aethra.local";
    private const string AdminPassword = "real-testcontainers-admin-password-123";

    [Fact]
    public async Task IdentitySeeder_real_with_SeedIdentityOnStart_creates_builtin_roles_and_admin_user()
    {
        // 1. Crear base de datos aislada en el PostgreSQL de Testcontainers
        var dbName = $"identity_seed_{Guid.NewGuid():N}";
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

        // 2. Configuración con SeedIdentityOnStart=true
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Aethra"] = connStr,
                ["Aethra:SeedIdentityOnStart"] = "true",
                ["Identity:AdminEmail"] = AdminEmail,
                ["Identity:AdminPasswordSeed"] = AdminPassword,
            })
            .Build();

        // Verificar la bandera de arranque
        var shouldSeed = config.GetValue("Aethra:SeedIdentityOnStart", false);
        shouldSeed.Should().BeTrue("Aethra:SeedIdentityOnStart=true debe habilitar el sembrado de identidad al arrancar");

        // 3. Registrar servicios reales de IdentityModule
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        services.AddDataProtection();
        services.AddSingleton<IClock, SystemClock>();
        services.AddIdentityModule(config);

        await using var sp = services.BuildServiceProvider();

        // 4. Aplicar migraciones reales de IdentityDbContext en PostgreSQL
        var db = sp.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        // 5. Ejecutar el IdentitySeeder real
        var seeder = sp.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync();

        // 6. Comprobar que crea roles y admin
        var roles = await db.Roles.AsNoTracking().ToListAsync();
        roles.Should().HaveCount(3);
        roles.Select(r => r.Slug).Should().BeEquivalentTo([Role.AdminSlug, Role.DeveloperSlug, Role.ViewerSlug]);
        roles.Should().OnlyContain(r => r.IsSystem);

        var adminRole = roles.Single(r => r.Slug == Role.AdminSlug);
        adminRole.Scopes.Should().Contain(ApiKey.AdminScope);

        var users = await db.Users.Include(u => u.Roles).AsNoTracking().ToListAsync();
        users.Should().HaveCount(1);

        var adminUser = users.Single();
        adminUser.Email.Should().Be(AdminEmail);
        adminUser.Roles.Should().HaveCount(1);
        adminUser.Roles.Single().RoleId.Should().Be(adminRole.Id);

        var passwordCodec = sp.GetRequiredService<IUserPasswordCodec>();
        passwordCodec.Verify(AdminPassword, adminUser.PasswordHashProtected).Should().BeTrue(
            "la contraseña sembrada debe coincidir con la configurada en Identity:AdminPasswordSeed");

        // 7. Idempotencia: una segunda ejecución no duplica roles ni usuarios
        await seeder.SeedAsync();

        var rolesAfter = await db.Roles.AsNoTracking().CountAsync();
        var usersAfter = await db.Users.AsNoTracking().CountAsync();
        rolesAfter.Should().Be(3);
        usersAfter.Should().Be(1);
    }
}
