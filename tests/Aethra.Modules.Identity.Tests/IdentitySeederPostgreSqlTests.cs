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

    private async Task<string> CreateIsolatedDatabaseAsync()
    {
        var dbName = $"identity_seed_{Guid.NewGuid():N}";
        await using (var masterConn = new NpgsqlConnection(postgres.ConnectionString))
        {
            await masterConn.OpenAsync();
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = dbName,
        }.ConnectionString;
    }

    private static ServiceProvider BuildServiceProvider(
        string connStr,
        string? adminPasswordSeed,
        Action<IServiceCollection>? configureServices = null)
    {
        var configDict = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Aethra"] = connStr,
            ["Aethra:SeedIdentityOnStart"] = "true",
            ["Identity:AdminEmail"] = AdminEmail,
        };
        if (adminPasswordSeed is not null)
        {
            configDict["Identity:AdminPasswordSeed"] = adminPasswordSeed;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        services.AddDataProtection();
        services.AddSingleton<IClock, SystemClock>();
        services.AddIdentityModule(config);
        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task IdentitySeeder_real_with_SeedIdentityOnStart_creates_builtin_roles_and_admin_user()
    {
        var connStr = await CreateIsolatedDatabaseAsync();
        await using var sp = BuildServiceProvider(connStr, AdminPassword);

        var db = sp.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var seeder = sp.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync();

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
        passwordCodec.Verify(AdminPassword, adminUser.PasswordHashCipher).Should().BeTrue(
            "la contraseña sembrada debe coincidir con la configurada en Identity:AdminPasswordSeed");

        // Idempotencia: una segunda ejecución no duplica roles ni usuarios
        await seeder.SeedAsync();

        var rolesAfter = await db.Roles.AsNoTracking().CountAsync();
        var usersAfter = await db.Users.AsNoTracking().CountAsync();
        rolesAfter.Should().Be(3);
        usersAfter.Should().Be(1);
    }

    [Fact]
    public async Task IdentitySeeder_without_seed_creates_builtin_roles_zero_users_and_logs_warning_with_event_id()
    {
        var connStr = await CreateIsolatedDatabaseAsync();
        var logRecords = new List<TestLogRecord>();

        await using var sp = BuildServiceProvider(
            connStr,
            adminPasswordSeed: null,
            services => services.AddSingleton<ILoggerProvider>(new TestLoggerProvider(logRecords)));

        var db = sp.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var seeder = sp.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync();

        var roles = await db.Roles.AsNoTracking().ToListAsync();
        roles.Should().HaveCount(3);
        roles.Select(r => r.Slug).Should().BeEquivalentTo([Role.AdminSlug, Role.DeveloperSlug, Role.ViewerSlug]);

        var users = await db.Users.AsNoTracking().ToListAsync();
        users.Should().BeEmpty("sin semilla explícita no debe crearse ningún usuario");

        var warning = logRecords.Should().ContainSingle(
            l => l.LogLevel == LogLevel.Warning && l.EventId.Id == IdentitySeeder.AdminSeedMissing.Id,
            "debe registrarse un LogWarning con el EventId AdminSeedMissing").Subject;

        warning.EventId.Name.Should().Be(IdentitySeeder.AdminSeedMissing.Name);
        warning.Message.Should().Contain("Identity__AdminPasswordSeed");
        warning.Message.Should().NotContain(
            "AllowBootstrapLogin",
            "sin semilla ni hash, el login de bootstrap acepta la contraseña por defecto de SingleUserStore");
    }

    [Fact]
    public async Task IdentitySeeder_regression_without_seed_no_user_created_with_default_password()
    {
        var connStr = await CreateIsolatedDatabaseAsync();
        await using var sp = BuildServiceProvider(connStr, adminPasswordSeed: null);

        var db = sp.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var seeder = sp.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync();

        var userCount = await db.Users.AsNoTracking().CountAsync();
        userCount.Should().Be(0, "sin semilla configurada no debe crearse ningún usuario por defecto ni con contraseña de fallback");

        var adminUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == AdminEmail);
        adminUser.Should().BeNull("ningún usuario admin debe existir cuando no se especifica una semilla");
    }

    [Fact]
    public async Task IdentitySeeder_concurrent_calls_against_empty_db_creates_exactly_one_admin_and_three_roles_without_exceptions()
    {
        var connStr = await CreateIsolatedDatabaseAsync();

        // 1. Preparar esquema migrado con base vacía
        await using (var setupSp = BuildServiceProvider(connStr, AdminPassword))
        {
            var setupDb = setupSp.GetRequiredService<IdentityDbContext>();
            await setupDb.Database.MigrateAsync();
        }

        // 2. Dos contenedores DI y seeders independientes sobre la misma base de datos
        await using var sp1 = BuildServiceProvider(connStr, AdminPassword);
        await using var sp2 = BuildServiceProvider(connStr, AdminPassword);

        var seeder1 = sp1.GetRequiredService<IdentitySeeder>();
        var seeder2 = sp2.GetRequiredService<IdentitySeeder>();

        // 3. Sincronizar entrada simultánea con una barrera
        using var barrier = new Barrier(2);

        var task1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await seeder1.SeedAsync();
        });

        var task2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await seeder2.SeedAsync();
        });

        await Task.WhenAll(task1, task2);

        // 4. Verificar exactamente 1 usuario y 3 roles en la BD
        var db = sp1.GetRequiredService<IdentityDbContext>();
        var roles = await db.Roles.AsNoTracking().ToListAsync();
        var users = await db.Users.AsNoTracking().ToListAsync();

        roles.Should().HaveCount(3);
        users.Should().HaveCount(1);
    }

    private sealed record TestLogRecord(LogLevel LogLevel, EventId EventId, string Message, Exception? Exception);

    private sealed class TestLoggerProvider(List<TestLogRecord> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(logs);
        public void Dispose() { }

        private sealed class ListLogger(List<TestLogRecord> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (logs)
                {
                    logs.Add(new TestLogRecord(logLevel, eventId, formatter(state, exception), exception));
                }
            }
        }
    }
}
