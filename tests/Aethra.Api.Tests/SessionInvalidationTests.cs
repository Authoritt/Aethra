using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Aethra.Api.Bootstrap;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Modules.Identity.UseCases.Totp;
using Aethra.Shared.Contracts.Authentication;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aethra.Api.Tests;

/// <summary>
/// Pruebas de integración del issue #82: invalidación de sesiones activas (cookies)
/// tras cambios de contraseña, desactivación de cuenta, RBAC o revocación explícita.
/// </summary>
public sealed class SessionInvalidationTests
{
    private const string Password = "password-12345678";
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Password_reset_invalidates_previously_issued_cookie_with_401()
    {
        var userRepo = new TestUserRepository();
        var roleRepo = new TestRoleRepository();
        using var factory = new SessionAuthTestFactory(userRepo, roleRepo);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        var (user, _) = CreateUserWithAdminRole(factory, userRepo, roleRepo, "user1@example.com");

        var cookie = await LoginAndGetCookieAsync(client, "user1@example.com", Password);

        // 1. Con la cookie emitida, la petición autenticada pasa con 200
        var resBefore = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookie);
        resBefore.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Resetear contraseña del usuario
        var codec = factory.Services.GetRequiredService<IUserPasswordCodec>();
        user.ResetPassword(codec.HashAndProtect("new-password-87654321"), DateTimeOffset.UtcNow);

        // 3. La SIGUIENTE petición con la MISMA cookie debe ser rechazada con 401 por CookieSessionValidator
        var resAfter = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookie);
        resAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        resAfter.Headers.Should().ContainKey("Set-Cookie");
        resAfter.Headers.GetValues("Set-Cookie").First().Should().Contain("aethra.sid=");
    }

    [Fact]
    public async Task Deactivating_user_invalidates_previously_issued_cookie_with_401()
    {
        var userRepo = new TestUserRepository();
        var roleRepo = new TestRoleRepository();
        using var factory = new SessionAuthTestFactory(userRepo, roleRepo);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        var (user, _) = CreateUserWithAdminRole(factory, userRepo, roleRepo, "user2@example.com");

        var cookie = await LoginAndGetCookieAsync(client, "user2@example.com", Password);

        var resBefore = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookie);
        resBefore.StatusCode.Should().Be(HttpStatusCode.OK);

        // Desactivar el usuario
        user.Deactivate(DateTimeOffset.UtcNow);

        // La siguiente petición da 401
        var resAfter = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookie);
        resAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Downgrading_role_scopes_updates_active_session_returning_403_for_removed_scope()
    {
        var userRepo = new TestUserRepository();
        var roleRepo = new TestRoleRepository();
        using var factory = new SessionAuthTestFactory(userRepo, roleRepo);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        // Rol custom 'operator' con scopes '*' (admin) y 'notes:read'
        var operatorRole = Role.CreateCustom("operator", "Operator", [ApiKey.AdminScope, "notes:read"], Now);
        roleRepo.Add(operatorRole);

        var codec = factory.Services.GetRequiredService<IUserPasswordCodec>();
        var user = User.Create("operator@example.com", codec.HashAndProtect(Password), "Operator", Now);
        user.AssignRole(operatorRole.Id, Now);
        userRepo.Add(user);

        var cookie = await LoginAndGetCookieAsync(client, "operator@example.com", Password);

        // 1. Con el scope '*', tiene acceso a /openapi/v1.json (exige '*')
        var resAdminBefore = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookie);
        resAdminBefore.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Modificar el rol en el repositorio para quitarle '*' (dejando solo 'notes:read')
        operatorRole.UpdateScopes(["notes:read"], Now.AddMinutes(5));

        // 3. La siguiente petición autenticada revalida scopes contra la BD.
        // El endpoint /openapi/v1.json que exigía '*' da 403 y re-emite la cookie con los nuevos scopes.
        var resForbidden = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookie);
        resForbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "al remover el scope '*' del rol, el endpoint que lo exigía debe responder 403");
        resForbidden.Headers.Should().ContainKey("Set-Cookie", "debe renovar la cookie con los nuevos scopes");

        var renewedCookie = resForbidden.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        // 4. Sin embargo, un endpoint que solo exige sesión válida (/dashboard vía proxy) sigue respondiendo 200
        var resDashboard = await SendAuthenticatedGetAsync(client, "/dashboard", renewedCookie);
        resDashboard.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revoke_sessions_invalidates_old_cookie_and_me_revoke_sessions_keeps_reissued_alive()
    {
        var userRepo = new TestUserRepository();
        var roleRepo = new TestRoleRepository();
        using var factory = new SessionAuthTestFactory(userRepo, roleRepo);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        var (user, _) = CreateUserWithAdminRole(factory, userRepo, roleRepo, "user3@example.com");

        // Simular dos sesiones (dos cookies) para el mismo usuario
        var session1Cookie = await LoginAndGetCookieAsync(client, "user3@example.com", Password);
        var session2Cookie = await LoginAndGetCookieAsync(client, "user3@example.com", Password);

        // Ambas sesiones funcionan inicialmente
        (await SendAuthenticatedGetAsync(client, "/openapi/v1.json", session1Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAuthenticatedGetAsync(client, "/openapi/v1.json", session2Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);

        // La sesión 1 llama a /auth/me/revoke-sessions
        using var revokeReq = new HttpRequestMessage(HttpMethod.Post, "/auth/me/revoke-sessions");
        revokeReq.Headers.Host = AethraApiFactory.PanelHost;
        revokeReq.Headers.Add("Cookie", session1Cookie);
        var revokeRes = await client.SendAsync(revokeReq);
        revokeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        revokeRes.Headers.Should().ContainKey("Set-Cookie");
        var reissuedCookie = revokeRes.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        // La cookie re-emitida sigue viva y funciona
        var resReissued = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", reissuedCookie);
        resReissued.StatusCode.Should().Be(HttpStatusCode.OK);

        // La sesión 2 (cookie vieja de otro dispositivo) queda invalidada con 401
        var resSession2 = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", session2Cookie);
        resSession2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // La sesión 1 con su cookie VIEJA previa a la re-emisión también queda invalidada con 401
        var resSession1Old = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", session1Cookie);
        resSession1Old.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Two_api_replicas_sharing_user_repo_and_data_protection_invalidate_cookies_cross_replica()
    {
        var sharedUserRepo = new TestUserRepository();
        var sharedRoleRepo = new TestRoleRepository();

        using var replica1 = new SessionAuthTestFactory(sharedUserRepo, sharedRoleRepo);
        using var replica2 = new SessionAuthTestFactory(sharedUserRepo, sharedRoleRepo);

        using var client1 = replica1.CreateClient(new() { AllowAutoRedirect = false });
        using var client2 = replica2.CreateClient(new() { AllowAutoRedirect = false });

        var (user, _) = CreateUserWithAdminRole(replica1, sharedUserRepo, sharedRoleRepo, "user4@example.com");

        // Login en replica1 -> emite cookie cifrada con Data Protection compartido
        var cookieFromReplica1 = await LoginAndGetCookieAsync(client1, "user4@example.com", Password);

        // Replica2 valida la cookie emitida por Replica1 con éxito
        var resReplica2Before = await SendAuthenticatedGetAsync(client2, "/openapi/v1.json", cookieFromReplica1);
        resReplica2Before.StatusCode.Should().Be(HttpStatusCode.OK);

        // En Replica1 (o en el repositorio compartido), se invalidan las sesiones del usuario
        user.RotateSecurityStamp(DateTimeOffset.UtcNow);

        // La SIGUIENTE petición en Replica2 con esa MISMA cookie debe ser rechazada con 401
        var resReplica2After = await SendAuthenticatedGetAsync(client2, "/openapi/v1.json", cookieFromReplica1);
        resReplica2After.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cookie_without_security_stamp_claim_is_rejected_with_401()
    {
        var userRepo = new TestUserRepository();
        var roleRepo = new TestRoleRepository();
        using var factory = new SessionAuthTestFactory(userRepo, roleRepo);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        var (user, role) = CreateUserWithAdminRole(factory, userRepo, roleRepo, "user5@example.com");

        // Emitir manualmente una cookie sin el claim 'aethra:security_stamp' (simulando cookie previa a #82)
        var claimsWithoutStamp = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName ?? user.Email),
            new(ClaimTypes.Role, role.Slug),
            new(ApiKeyAuthSchemes.ScopeClaim, ApiKey.AdminScope),
        };
        var identityWithoutStamp = new ClaimsIdentity(claimsWithoutStamp, AuthSchemes.Cookie);

        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AuthSchemes.Cookie);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identityWithoutStamp), new AuthenticationProperties { IsPersistent = true }, AuthSchemes.Cookie);
        var rawCookie = cookieOptions.TicketDataFormat.Protect(ticket);
        var cookieHeaderValue = $"aethra.sid={rawCookie}";

        // Petición con la cookie sin stamp -> 401 Unauthorized
        var res = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookieHeaderValue);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Removing_a_role_from_user_drops_its_scope_with_403_but_keeps_the_session()
    {
        var userRepo = new TestUserRepository();
        var roleRepo = new TestRoleRepository();
        using var factory = new SessionAuthTestFactory(userRepo, roleRepo);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        var adminRole = Role.CreateSystem(Role.AdminSlug, "Admin", [ApiKey.AdminScope], Now);
        var readerRole = Role.CreateCustom("reader", "Reader", ["notes:read"], Now);
        roleRepo.Add(adminRole);
        roleRepo.Add(readerRole);

        var codec = factory.Services.GetRequiredService<IUserPasswordCodec>();
        var user = User.Create("two-roles@example.com", codec.HashAndProtect(Password), "Two roles", Now);
        user.AssignRole(adminRole.Id, Now);
        user.AssignRole(readerRole.Id, Now);
        userRepo.Add(user);

        var cookie = await LoginAndGetCookieAsync(client, "two-roles@example.com", Password);
        (await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookie)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Quitar el rol admin al USUARIO (no cambiar el rol): pierde '*' pero conserva la sesion.
        user.UnassignRole(adminRole.Id, Now.AddMinutes(5));

        var resForbidden = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookie);
        resForbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "quitar un rol quita su permiso, no cierra la sesion (los cambios de roles no rotan el stamp)");
        resForbidden.Headers.Should().ContainKey("Set-Cookie", "debe renovar la cookie con los roles vigentes");
        var renewedCookie = resForbidden.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        (await SendAuthenticatedGetAsync(client, "/dashboard", renewedCookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enabling_totp_reissues_own_cookie_and_invalidates_other_sessions()
    {
        var userRepo = new TestUserRepository();
        var roleRepo = new TestRoleRepository();
        using var factory = new SessionAuthTestFactory(userRepo, roleRepo, configureServices: services =>
        {
            services.RemoveAll<IRequestHandler<VerifyTotpEnrollmentCommand, Result<TotpEnableResultDto>>>();
            services.AddTransient<IRequestHandler<VerifyTotpEnrollmentCommand, Result<TotpEnableResultDto>>, FakeVerifyTotpHandler>();
        });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        var (user, _) = CreateUserWithAdminRole(factory, userRepo, roleRepo, "totp-on@example.com");
        user.BeginTotpEnrollment([9, 9, 9], Now);

        var ownCookie = await LoginAndGetCookieAsync(client, "totp-on@example.com", Password);
        var otherCookie = await LoginAndGetCookieAsync(client, "totp-on@example.com", Password);

        var res = await SendAuthenticatedPostAsync(client, "/api/identity/me/totp/verify", ownCookie, new { code = "123456" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        user.TotpEnabled.Should().BeTrue();
        res.Headers.Should().ContainKey("Set-Cookie", "activar 2FA rota el stamp: la sesion propia se re-emite");
        var reissued = res.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        (await SendAuthenticatedGetAsync(client, "/openapi/v1.json", reissued)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAuthenticatedGetAsync(client, "/openapi/v1.json", otherCookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Disabling_totp_reissues_own_cookie_and_invalidates_other_sessions()
    {
        var userRepo = new TestUserRepository();
        var roleRepo = new TestRoleRepository();
        using var factory = new SessionAuthTestFactory(userRepo, roleRepo, configureServices: services =>
        {
            services.RemoveAll<IRequestHandler<DisableTotpCommand, Result>>();
            services.AddTransient<IRequestHandler<DisableTotpCommand, Result>, FakeDisableTotpHandler>();
        });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        var (_, _) = CreateUserWithAdminRole(factory, userRepo, roleRepo, "totp-off@example.com");

        var ownCookie = await LoginAndGetCookieAsync(client, "totp-off@example.com", Password);
        var otherCookie = await LoginAndGetCookieAsync(client, "totp-off@example.com", Password);

        var res = await SendAuthenticatedPostAsync(client, "/api/identity/me/totp/disable", ownCookie, new { code = "123456" });
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
        res.Headers.Should().ContainKey("Set-Cookie", "desactivar 2FA rota el stamp: la sesion propia se re-emite");
        var reissued = res.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        (await SendAuthenticatedGetAsync(client, "/openapi/v1.json", reissued)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAuthenticatedGetAsync(client, "/openapi/v1.json", otherCookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SessionValidationIntervalSeconds_skips_db_lookup_within_window()
    {
        var userRepo = new TestUserRepository();
        var roleRepo = new TestRoleRepository();
        var testClock = new TestClock(Now);

        using var factory = new SessionAuthTestFactory(
            userRepo,
            roleRepo,
            testClock,
            new Dictionary<string, string?>
            {
                ["Identity:SessionValidationIntervalSeconds"] = "60",
            });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

        var (user, _) = CreateUserWithAdminRole(factory, userRepo, roleRepo, "user6@example.com");

        var cookie = await LoginAndGetCookieAsync(client, "user6@example.com", Password);

        // Primera petición: valida contra BD y marca timestamp en ticket
        var res1 = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", cookie);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var renewedCookie = res1.Headers.Contains("Set-Cookie")
            ? res1.Headers.GetValues("Set-Cookie").First().Split(';')[0]
            : cookie;

        // Cambiar stamp en BD inmediatamente
        var oldStamp = user.SecurityStamp;
        user.RotateSecurityStamp(testClock.UtcNow);

        // Dentro de la ventana de 60s (30s después), omite consulta a BD -> responde 200
        testClock.Advance(TimeSpan.FromSeconds(30));
        var resWithinWindow = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", renewedCookie);
        resWithinWindow.StatusCode.Should().Be(HttpStatusCode.OK);

        // Pasada la ventana (70s después), revalida contra BD -> detecta cambio de stamp -> 401
        testClock.Advance(TimeSpan.FromSeconds(40));
        var resAfterWindow = await SendAuthenticatedGetAsync(client, "/openapi/v1.json", renewedCookie);
        resAfterWindow.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static (User User, Role AdminRole) CreateUserWithAdminRole(
        AethraApiFactory factory,
        TestUserRepository userRepo,
        TestRoleRepository roleRepo,
        string email)
    {
        var adminRole = Role.CreateSystem(Role.AdminSlug, "Admin", [ApiKey.AdminScope], Now);
        roleRepo.Add(adminRole);

        var codec = factory.Services.GetRequiredService<IUserPasswordCodec>();
        var user = User.Create(email, codec.HashAndProtect(Password), "Admin User", Now);
        user.AssignRole(adminRole.Id, Now);
        userRepo.Add(user);

        return (user, adminRole);
    }

    private static async Task<string> LoginAndGetCookieAsync(HttpClient client, string email, string password)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new { email, password }),
            Encoding.UTF8,
            "application/json");
        using var loginReq = new HttpRequestMessage(HttpMethod.Post, "/auth/login") { Content = content };
        loginReq.Headers.Host = AethraApiFactory.PanelHost;
        loginReq.Headers.Add(AethraApiFactory.PeerHeader, AethraApiFactory.BridgeGateway);

        var response = await client.SendAsync(loginReq);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "el login con credenciales válidas debe responder 200");
        response.Headers.Should().ContainKey("Set-Cookie");
        var cookieHeader = response.Headers.GetValues("Set-Cookie").First();
        return cookieHeader.Split(';')[0];
    }

    private static async Task<HttpResponseMessage> SendAuthenticatedGetAsync(
        HttpClient client,
        string uri,
        string cookieValue)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Host = AethraApiFactory.PanelHost;
        req.Headers.Add("Cookie", cookieValue);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> SendAuthenticatedPostAsync(
        HttpClient client,
        string uri,
        string cookieValue,
        object body)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
        req.Headers.Host = AethraApiFactory.PanelHost;
        req.Headers.Add("Cookie", cookieValue);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> SendAuthenticatedPatchAsync(
        HttpClient client,
        string uri,
        string cookieValue,
        object body)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Patch, uri) { Content = content };
        req.Headers.Host = AethraApiFactory.PanelHost;
        req.Headers.Add("Cookie", cookieValue);
        return await client.SendAsync(req);
    }

    private sealed class SessionAuthTestFactory : AethraApiFactory
    {
        private readonly TestUserRepository _userRepo;
        private readonly TestRoleRepository _roleRepo;
        private readonly IClock? _clock;
        private readonly Dictionary<string, string?> _configuration;
        private readonly Action<IServiceCollection>? _configureServices;

        public SessionAuthTestFactory(
            TestUserRepository userRepo,
            TestRoleRepository roleRepo,
            IClock? clock = null,
            Dictionary<string, string?>? configuration = null,
            Action<IServiceCollection>? configureServices = null)
        {
            _userRepo = userRepo;
            _roleRepo = roleRepo;
            _clock = clock;
            _configuration = configuration ?? [];
            _configureServices = configureServices;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                if (_configuration.Count > 0)
                {
                    config.AddInMemoryCollection(_configuration);
                }
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(_userRepo);

                services.RemoveAll<IRoleRepository>();
                services.AddSingleton<IRoleRepository>(_roleRepo);

                services.RemoveAll<IAuthenticationAttemptLimiter>();
                services.AddSingleton<IAuthenticationAttemptLimiter, PermissiveAttemptLimiter>();

                if (_clock is not null)
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton<IClock>(_clock);
                }

                _configureServices?.Invoke(services);
            });
        }
    }

    // Sustitutos de los handlers TOTP (los reales usan IdentityDbContext, que esta fabrica no tiene):
    // aplican el MISMO metodo de dominio, que es el que rota el stamp.
    private sealed class FakeVerifyTotpHandler(IUserRepository users)
        : IRequestHandler<VerifyTotpEnrollmentCommand, Result<TotpEnableResultDto>>
    {
        public async Task<Result<TotpEnableResultDto>> Handle(VerifyTotpEnrollmentCommand request, CancellationToken ct)
        {
            AethraId.TryParse(request.UserId, out var id).Should().BeTrue();
            var user = await users.GetByIdAsync(new UserId(id!.Value), ct);
            user!.CompleteTotpEnrollment([1, 2, 3], DateTimeOffset.UtcNow);
            return Result.Success(new TotpEnableResultDto(true, ["recovery-1"]));
        }
    }

    private sealed class FakeDisableTotpHandler(IUserRepository users)
        : IRequestHandler<DisableTotpCommand, Result>
    {
        public async Task<Result> Handle(DisableTotpCommand request, CancellationToken ct)
        {
            AethraId.TryParse(request.UserId, out var id).Should().BeTrue();
            var user = await users.GetByIdAsync(new UserId(id!.Value), ct);
            user!.DisableTotp(DateTimeOffset.UtcNow);
            return Result.Success();
        }
    }

    private sealed class PermissiveAttemptLimiter : IAuthenticationAttemptLimiter
    {
        public ValueTask<AuthenticationAttemptLease> AcquirePasswordAsync(string email, CancellationToken cancellationToken)
            => ValueTask.FromResult(new AuthenticationAttemptLease(true, 1, DateTimeOffset.UtcNow.AddMinutes(5)));

        public ValueTask ResetPasswordAsync(string email, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class TestUserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<UserId, User> _users = new();

        public void Add(User user) => _users[user.Id] = user;

        public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
        {
            var normalized = User.NormalizeEmail(email);
            return Task.FromResult(_users.Values.FirstOrDefault(u => string.Equals(u.Email, normalized, StringComparison.Ordinal)));
        }

        public Task<User?> GetByIdAsync(UserId id, CancellationToken ct)
        {
            _users.TryGetValue(id, out var user);
            return Task.FromResult(user);
        }

        public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<User>>(_users.Values.ToList());

        public Task<int> CountAsync(CancellationToken ct)
            => Task.FromResult(_users.Count);
    }

    private sealed class TestRoleRepository : IRoleRepository
    {
        private readonly ConcurrentDictionary<RoleId, Role> _roles = new();

        public void Add(Role role) => _roles[role.Id] = role;

        public Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct)
        {
            _roles.TryGetValue(id, out var role);
            return Task.FromResult(role);
        }

        public Task<Role?> FindBySlugAsync(string slug, CancellationToken ct)
        {
            var normalized = slug.Trim().ToLowerInvariant();
            return Task.FromResult(_roles.Values.FirstOrDefault(r => string.Equals(r.Slug, normalized, StringComparison.Ordinal)));
        }

        public Task<IReadOnlyList<Role>> ListByIdsAsync(IEnumerable<RoleId> ids, CancellationToken ct)
        {
            var idSet = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<Role>>(_roles.Values.Where(r => idSet.Contains(r.Id)).ToList());
        }

        public Task<IReadOnlyList<Role>> ListAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Role>>(_roles.Values.ToList());

        public Task<int> CountAsync(CancellationToken ct)
            => Task.FromResult(_roles.Count);
    }

    private sealed class TestClock(DateTimeOffset initial) : IClock
    {
        private DateTimeOffset _current = initial;

        public DateTimeOffset UtcNow => _current;

        public void Advance(TimeSpan delta) => _current = _current.Add(delta);
    }
}
