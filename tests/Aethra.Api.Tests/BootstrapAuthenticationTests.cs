using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aethra.Api.Bootstrap;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// Pruebas del issue #21 de seguridad: ciclo de vida y postura de autenticación bootstrap.
/// Cubre:
/// 1. Despliegue en producción con migraciones externas y tabla de usuarios vacía (comportamiento seguro por defecto: 503).
/// 2. Habilitación explícita mediante Identity:AllowBootstrapLogin=true cuando la tabla está vacía.
/// 3. Rechazo de credenciales incorrectas en la rama bootstrap (401).
/// 4. Modo Development: habilitación por defecto cuando no hay usuarios.
/// 5. Rechazo inmediato de credenciales bootstrap una vez existe un admin persistido en BD (cierre del ciclo de vida).
/// 6. Desacoplamiento de IdentitySeeder respecto a ApplyMigrationsOnStart (Aethra:SeedIdentityOnStart).
/// 7. Garantía de auditoría/logs sin filtración de contraseñas o secretos.
/// </summary>
public sealed class BootstrapAuthenticationTests
{
    private const string AdminEmail = "admin@aethra.local";
    private const string BootstrapPassword = "secret-bootstrap-token-p1-xyz";

    [Fact]
    public async Task Production_with_empty_users_and_default_flag_fails_safely_with_503()
    {
        using var factory = new BootstrapAuthTestFactory(Environments.Production);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, AdminEmail, BootstrapPassword);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "en producción con tabla vacía y AllowBootstrapLogin=false por defecto, el login bootstrap debe estar deshabilitado");

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body);
        json?["error"]?.GetValue<string>().Should().Be("bootstrap_login_disabled");

        factory.CapturedLogs.Entries.Should().Contain(e => e.EventId.Id == 2100 && e.EventId.Name == "BootstrapLoginAttempted");
        factory.CapturedLogs.Entries.Should().Contain(e => e.EventId.Id == 2101 && e.EventId.Name == "BootstrapLoginRejected");
        factory.CapturedLogs.Entries.Should().NotContain(e => e.Message.Contains(BootstrapPassword),
            "ningún log debe contener la contraseña intentada");
    }

    [Fact]
    public async Task Production_with_empty_users_and_explicit_flag_allows_bootstrap_login()
    {
        using var factory = new BootstrapAuthTestFactory(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["Identity:AllowBootstrapLogin"] = "true",
                ["Identity:AdminPasswordSeed"] = BootstrapPassword,
            });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, AdminEmail, BootstrapPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "con AllowBootstrapLogin=true y credenciales correctas en instalación vacía, el login bootstrap debe conceder sesión");

        response.Headers.Should().ContainKey("Set-Cookie");
        var cookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault();
        cookie.Should().NotBeNull().And.Contain("aethra.sid");

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body);
        json?["email"]?.GetValue<string>().Should().Be(AdminEmail);
        json?["requires_totp"]?.GetValue<bool>().Should().BeFalse();

        factory.CapturedLogs.Entries.Should().Contain(e => e.EventId.Id == 2100 && e.EventId.Name == "BootstrapLoginAttempted");
        factory.CapturedLogs.Entries.Should().Contain(e => e.EventId.Id == 2103 && e.EventId.Name == "BootstrapLoginSucceeded");
        factory.CapturedLogs.Entries.Should().NotContain(e => e.Message.Contains(BootstrapPassword));
    }

    [Fact]
    public async Task Production_with_empty_users_and_explicit_flag_rejects_invalid_credentials_with_401()
    {
        const string wrongPassword = "wrong-bootstrap-password-42";
        using var factory = new BootstrapAuthTestFactory(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["Identity:AllowBootstrapLogin"] = "true",
                ["Identity:AdminPasswordSeed"] = BootstrapPassword,
            });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, AdminEmail, wrongPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body);
        json?["error"]?.GetValue<string>().Should().Be("invalid_credentials");

        factory.CapturedLogs.Entries.Should().Contain(e => e.EventId.Id == 2100 && e.EventId.Name == "BootstrapLoginAttempted");
        factory.CapturedLogs.Entries.Should().Contain(e => e.EventId.Id == 2102 && e.EventId.Name == "BootstrapLoginInvalidCredentials");
        factory.CapturedLogs.Entries.Should().NotContain(e => e.Message.Contains(wrongPassword));
    }

    [Fact]
    public async Task Development_environment_enables_bootstrap_login_by_default_when_empty()
    {
        using var factory = new BootstrapAuthTestFactory(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["Aethra:ApplyMigrationsOnStart"] = "false", // pruebas sin BD local
                ["Identity:AdminPasswordSeed"] = "dev-seed-password-123",
            });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, AdminEmail, "dev-seed-password-123");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "en Development el login bootstrap debe estar habilitado por defecto cuando no hay usuarios");

        response.Headers.Should().ContainKey("Set-Cookie");
        var cookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault();
        cookie.Should().NotBeNull().And.Contain("aethra.sid");

        factory.CapturedLogs.Entries.Should().Contain(e => e.EventId.Id == 2100 && e.EventId.Name == "BootstrapLoginAttempted");
        factory.CapturedLogs.Entries.Should().Contain(e => e.EventId.Id == 2103 && e.EventId.Name == "BootstrapLoginSucceeded");
    }

    [Fact]
    public async Task Once_persisted_admin_exists_bootstrap_credentials_are_rejected_even_if_flag_is_true()
    {
        using var factory = new BootstrapAuthTestFactory(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["Identity:AllowBootstrapLogin"] = "true",
                ["Identity:AdminPasswordSeed"] = BootstrapPassword,
            });

        // Simular que el aprovisionamiento inicial ya ocurrió y existe un usuario en BD con otra contraseña.
        var codec = factory.Services.GetRequiredService<IUserPasswordCodec>();
        var persistedUser = User.Create(
            AdminEmail,
            codec.HashAndProtect("real-persisted-admin-password-99"),
            displayName: "Admin Real",
            DateTimeOffset.UtcNow);
        factory.UserRepository.Add(persistedUser);

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // Intentar autenticar con la credencial de bootstrap (que ya no debe tener validez)
        var response = await PostLoginAsync(client, AdminEmail, BootstrapPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "una vez existen usuarios en BD, la credencial de bootstrap debe rechazarse y validarse contra BD");

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body);
        json?["error"]?.GetValue<string>().Should().Be("invalid_credentials");

        // Al haber usuarios en BD, el servicio de bootstrap devuelve UsePersistedUsers sin intentar la rama bootstrap
        factory.CapturedLogs.Entries.Should().NotContain(e => e.EventId.Id == 2100,
            "no debe intentarse la rama de bootstrap cuando ya existen usuarios persistidos");
    }

    [Fact]
    public void Configuration_defaults_for_seeding_and_bootstrap_are_safe_by_default()
    {
        var config = new ConfigurationBuilder().Build();

        MigrationsBootstrap.ShouldSeedIdentity(config).Should().BeFalse(
            "en despliegues sin configuración explícita, el sembrado de identidad no debe ejecutarse en el aire");
        config.GetValue("Identity:AllowBootstrapLogin", false).Should().BeFalse(
            "en producción el login bootstrap debe ser false por defecto");
    }

    [Fact]
    public void Configuration_explicit_flags_are_respected()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aethra:SeedIdentityOnStart"] = "true",
                ["Identity:AllowBootstrapLogin"] = "true",
            })
            .Build();

        MigrationsBootstrap.ShouldSeedIdentity(config).Should().BeTrue();
        config.GetValue("Identity:AllowBootstrapLogin", false).Should().BeTrue();
    }

    [Fact]
    public void Startup_with_SeedIdentityOnStart_true_executes_seed_step()
    {
        var seedStep = new TestIdentitySeedStep();
        using var factory = new SeedStepTestAppFactory(seedStep, seedFlag: "true");
        using var client = factory.CreateClient();

        seedStep.Executed.Should().BeTrue("cuando SeedIdentityOnStart=true y ApplyMigrationsOnStart=false, el sembrado debe ejecutarse");
    }

    [Fact]
    public void Startup_with_SeedIdentityOnStart_false_omits_seed_step()
    {
        var seedStep = new TestIdentitySeedStep();
        using var factory = new SeedStepTestAppFactory(seedStep, seedFlag: "false");
        using var client = factory.CreateClient();

        seedStep.Executed.Should().BeFalse("cuando SeedIdentityOnStart=false y ApplyMigrationsOnStart=false, el sembrado debe omitirse");
    }

    [Fact]
    public async Task SeedIdentityAsync_extension_executes_registered_step()
    {
        var seedStep = new TestIdentitySeedStep();
        using var factory = new SeedStepTestAppFactory(seedStep, seedFlag: "false");
        _ = factory.CreateClient();

        await factory.Services.SeedIdentityAsync();

        seedStep.Executed.Should().BeTrue("invocar SeedIdentityAsync delega en el step registrado");
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password,
        string host = AethraApiFactory.PanelHost,
        string peer = AethraApiFactory.BridgeGateway)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email, password }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Host = host;
        request.Headers.Add(AethraApiFactory.PeerHeader, peer);
        return await client.SendAsync(request);
    }

    private sealed class TestIdentitySeedStep : IIdentitySeedStep
    {
        public bool Executed { get; private set; }

        public Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            Executed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class SeedStepTestAppFactory(
        TestIdentitySeedStep step,
        string seedFlag) : AethraApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Aethra:SeedIdentityOnStart"] = seedFlag,
                    ["Aethra:ApplyMigrationsOnStart"] = "false",
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IIdentitySeedStep>(step);
            });
        }
    }

    private sealed class BootstrapAuthTestFactory : AethraApiFactory
    {
        private readonly string _environment;
        private readonly Dictionary<string, string?> _configuration;

        public TestUserRepository UserRepository { get; } = new();
        public CaptureBootstrapLogsProvider CapturedLogs { get; } = new();

        public BootstrapAuthTestFactory(
            string environment,
            Dictionary<string, string?>? configuration = null)
        {
            _environment = environment;
            _configuration = configuration ?? [];
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(_environment);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                if (_configuration.Count > 0)
                {
                    config.AddInMemoryCollection(_configuration);
                }
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILoggerFactory>();
                services.AddSingleton<ILoggerFactory>(_ =>
                    LoggerFactory.Create(logging => logging.AddProvider(CapturedLogs)));

                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(UserRepository);

                services.RemoveAll<IAuthenticationAttemptLimiter>();
                services.AddSingleton<IAuthenticationAttemptLimiter, TestAttemptLimiter>();
            });
        }
    }

    private sealed class TestAttemptLimiter : IAuthenticationAttemptLimiter
    {
        public ValueTask<AuthenticationAttemptLease> AcquirePasswordAsync(string email, CancellationToken cancellationToken)
            => ValueTask.FromResult(new AuthenticationAttemptLease(true, 1, DateTimeOffset.UtcNow.AddMinutes(5)));

        public ValueTask ResetPasswordAsync(string email, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class TestUserRepository : IUserRepository
    {
        private readonly List<User> _users = [];

        public void Add(User user) => _users.Add(user);
        public void Clear() => _users.Clear();

        public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
        {
            var normalized = User.NormalizeEmail(email);
            return Task.FromResult(_users.FirstOrDefault(u => string.Equals(u.Email, normalized, StringComparison.Ordinal)));
        }

        public Task<User?> GetByIdAsync(UserId id, CancellationToken ct)
            => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

        public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<User>>(_users.ToList());

        public Task<int> CountAsync(CancellationToken ct)
            => Task.FromResult(_users.Count);
    }

    private sealed class CaptureBootstrapLogsProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

        public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _entries);

        public void Clear()
        {
            while (_entries.TryDequeue(out _)) { }
        }

        public void Dispose() { }

        public sealed record CapturedLogEntry(
            string Category,
            LogLevel Level,
            EventId EventId,
            string Message);

        private sealed class CaptureLogger(string categoryName, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
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
                entries.Enqueue(new CapturedLogEntry(categoryName, logLevel, eventId, formatter(state, exception)));
            }
        }
    }
}
