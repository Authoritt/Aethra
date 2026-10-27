using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Aethra.Api.Bootstrap;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
/// 4. Modo Development: con bandera ausente/false → 503 bootstrap_login_disabled (B1).
/// 5. Rechazo inmediato de credenciales bootstrap una vez existe un admin persistido en BD.
/// 6. Inmediata invalidación de sesión/cookie activa de bootstrap en cuanto existe el primer usuario persistido (M2).
/// 7. Desacoplamiento de IdentitySeeder respecto a ApplyMigrationsOnStart (Aethra:SeedIdentityOnStart).
/// 8. Garantía de auditoría/logs sin filtración de contraseñas o secretos.
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
    public async Task Development_environment_with_flag_absent_returns_503_bootstrap_login_disabled()
    {
        // B1: Demuestra que en Development con la bandera ausente/false, el login bootstrap
        // responde 503 bootstrap_login_disabled (no habilitado por defecto).
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddRouting();
        builder.Services.AddAuthentication(AuthSchemes.Cookie)
            .AddCookie(AuthSchemes.Cookie, _ => { });
        builder.Services.Configure<IdentityOptions>(_ => { });
        builder.Services.AddSingleton<SingleUserStore>();
        builder.Services.AddScoped<BootstrapAuthenticationService>();
        builder.Services.AddScoped<IUserRepository, TestUserRepository>();
        builder.Services.AddScoped<IUserPasswordCodec, StubPasswordCodec>();
        builder.Services.AddScoped<IClock, SystemClock>();
        builder.Services.AddScoped<IAuthenticationAttemptLimiter, TestAttemptLimiter>();
        builder.Services.AddScoped<EfUserStore>();
        builder.Services.AddScoped<IRoleRepository, StubRoleRepository>();
        builder.Services.AddSingleton<ITotpChallengeTokens, StubTotpChallengeTokens>();
        builder.Services.AddDbContext<IdentityDbContext>(o =>
            o.UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none"));
        builder.Services.AddRateLimiter(o =>
            o.AddPolicy(LoginRateLimiting.PolicyName, _ => RateLimitPartition.GetNoLimiter("test")));

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapAuthEndpoints();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var response = await client.PostAsync("/auth/login", new StringContent(
            JsonSerializer.Serialize(new { email = AdminEmail, password = "dev-seed-password-123" }),
            Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "en entorno Development con bandera ausente/false, el login bootstrap debe responder 503 bootstrap_login_disabled");

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body);
        json?["error"]?.GetValue<string>().Should().Be("bootstrap_login_disabled");
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
    public async Task Bootstrap_session_cookie_does_not_survive_creation_of_first_persisted_user()
    {
        // M2: Regresión — una sesión (cookie) obtenida por bootstrap NO debe sobrevivir a la existencia del primer usuario.
        // OnValidatePrincipal debe invalidar la identidad (RejectPrincipal + SignOut) en cuanto identity.users ya tiene filas.
        using var factory = new BootstrapAuthTestFactory(
            "Testing",
            new Dictionary<string, string?>
            {
                ["Identity:AllowBootstrapLogin"] = "true",
                ["Identity:AdminPasswordSeed"] = BootstrapPassword,
            });

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // 1. Login vía bootstrap cuando no hay usuarios
        var loginResponse = await PostLoginAsync(client, AdminEmail, BootstrapPassword);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        loginResponse.Headers.Should().ContainKey("Set-Cookie");
        var cookieHeader = loginResponse.Headers.GetValues("Set-Cookie").First();
        var cookieValue = cookieHeader.Split(';')[0]; // "aethra.sid=..."

        // 2. Mientras no haya usuarios, la cookie es válida y accede a un recurso protegido (/openapi/v1.json requiere AdminScope)
        using var requestBefore = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        requestBefore.Headers.Host = AethraApiFactory.PanelHost;
        requestBefore.Headers.Add("Cookie", cookieValue);

        var responseBefore = await client.SendAsync(requestBefore);
        responseBefore.StatusCode.Should().Be(HttpStatusCode.OK,
            "mientras la tabla de usuarios esté vacía, la sesión bootstrap es válida");

        // 3. Crear el primer usuario persistido en el repositorio
        var codec = factory.Services.GetRequiredService<IUserPasswordCodec>();
        var firstUser = User.Create(
            AdminEmail,
            codec.HashAndProtect("real-persisted-password-99"),
            displayName: "Admin Real",
            DateTimeOffset.UtcNow);
        factory.UserRepository.Add(firstUser);
        (await factory.UserRepository.CountAsync(CancellationToken.None)).Should().Be(1);

        // 4. La SIGUIENTE petición con la MISMA cookie bootstrap debe ser rechazada por OnValidatePrincipal
        using var requestAfter = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        requestAfter.Headers.Host = AethraApiFactory.PanelHost;
        requestAfter.Headers.Add("Cookie", cookieValue);

        var responseAfter = await client.SendAsync(requestAfter);
        responseAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "una vez creado el primer usuario, la sesión de bootstrap debe ser invalidada inmediatamente por OnValidatePrincipal");

        // 5. Debe emitir cabecera de Set-Cookie expirando aethra.sid (SignOut)
        responseAfter.Headers.Should().ContainKey("Set-Cookie");
        var expiredCookie = responseAfter.Headers.GetValues("Set-Cookie").First();
        expiredCookie.Should().Contain("aethra.sid=");
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
            string environment = "Testing",
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

    private sealed class StubPasswordCodec : IUserPasswordCodec
    {
        public byte[] HashAndProtect(string plainPassword) => Encoding.UTF8.GetBytes(plainPassword);
        public bool Verify(string plainPassword, byte[] storedCipher) => true;
    }

    private sealed class StubRoleRepository : IRoleRepository
    {
        public Task<Role?> FindBySlugAsync(string slug, CancellationToken ct) => Task.FromResult<Role?>(null);
        public Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct) => Task.FromResult<Role?>(null);
        public Task<IReadOnlyList<Role>> ListByIdsAsync(IReadOnlyCollection<RoleId> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<Role>>([]);
        public Task<IReadOnlyList<Role>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Role>>([]);
        public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class StubTotpChallengeTokens : ITotpChallengeTokens
    {
        public string Issue(string userId) => Guid.NewGuid().ToString("N");
        public string? ValidateAndGetUserId(string token) => null;
        public string? GetLastValidationError() => null;
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
