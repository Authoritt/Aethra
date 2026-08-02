using System.Security.Claims;
using System.Text.Json.Serialization;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Authentication;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Contracts.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Aethra.Api.Bootstrap;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string Email, string Password);

    /// <summary>
    /// F12.1B — body del segundo paso del login. Acepta tanto <c>totp_token</c> (snake_case,
    /// recomendado por el plan F12.1) como <c>totpToken</c> (camelCase, default ASP.NET).
    /// </summary>
    public sealed record TotpLoginRequest(
        [property: JsonPropertyName("totp_token")] string TotpToken,
        string Code);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (
            [FromBody] LoginRequest req,
            EfUserStore userStore,
            IRoleRepository roleRepo,
            SingleUserStore singleUserStore,
            ITotpChallengeTokens totpTokens,
            HttpContext http,
            IdentityDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            // F11.1: si hay users en BD, validar contra EfUserStore. Si está vacía,
            // fallback a SingleUserStore (bootstrap inicial). Tras login con fallback
            // emitimos claims equivalentes a admin para que el primer login pueda crear users.
            var hasUsers = await userStore.CountAsync(ct) > 0;

            ClaimsIdentity identity;
            if (hasUsers)
            {
                var user = await userStore.ValidateCredentialsAsync(req.Email, req.Password, ct);
                if (user is null)
                {
                    return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
                }

                // F12.1B: si el usuario tiene 2FA activo, NO firmamos cookie todavia. Devolvemos
                // un challenge token corto que el cliente debe completar con un TOTP code via
                // POST /auth/login/totp. El password ya valido pero el segundo factor pendiente.
                if (user.TotpEnabled)
                {
                    var challenge = totpTokens.Issue(user.Id.ToString());
                    return Results.Ok(new
                    {
                        requires_totp = true,
                        totp_token = challenge,
                        email = user.Email,
                    });
                }

                // Persistir LastLoginAt — MarkLogin ya se invocó dentro del store.
                await db.SaveChangesAsync(ct);

                identity = BuildIdentityForUser(user, await roleRepo.ListByIdsAsync(
                    [.. user.Roles.Select(r => r.RoleId)], ct));
            }
            else
            {
                if (!singleUserStore.ValidateCredentials(req.Email, req.Password))
                {
                    return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
                }
                // Esta rama concede ADMIN con una credencial de configuracion. Es correcta
                // ANTES del primer usuario y deberia dejar de alcanzarse en cuanto exista uno.
                // Se registra en Warning porque, si se toma en un sistema ya en uso, significa
                // que la tabla de users esta vacia cuando no deberia -- y hasta ahora eso
                // ocurria en silencio. Un fallback que nadie observa ejecutarse no se distingue
                // de uno que se ejecuta siempre. Ver issue #21.
                loggerFactory.CreateLogger("Aethra.Auth.Bootstrap").LogWarning(
                    "Login por CREDENCIAL DE BOOTSTRAP para {Email}: la tabla de users esta vacia, "
                    + "asi que se conceden claims de admin desde configuracion. Si esto no es una "
                    + "instalacion nueva, el sembrador no corrio (Aethra__ApplyMigrationsOnStart) y "
                    + "la credencial semilla sigue viva.",
                    singleUserStore.AdminEmail);

                // Bootstrap: el usuario admin del config aún no existe en BD. Emitimos claims
                // con role admin para permitir que llame /api/identity/users y cree users reales.
                identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Email, singleUserStore.AdminEmail),
                    new Claim(ClaimTypes.Name, singleUserStore.AdminEmail),
                    new Claim(ClaimTypes.Role, Role.AdminSlug),
                    new Claim(ApiKeyAuthSchemes.ScopeClaim, ApiKey.AdminScope),
                ], AuthSchemes.Cookie);
            }

            await http.SignInAsync(AuthSchemes.Cookie, new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                IsPersistent = true,
            });

            return Results.Ok(new
            {
                email = identity.FindFirst(ClaimTypes.Email)?.Value,
                requires_totp = false,
            });
        })
        .WithName("Login")
        .AllowAnonymous();

        // F12.1B: segundo paso del login para users con 2FA activo. Valida el JWT challenge
        // + el codigo TOTP (o recovery). Si OK, emite la cookie completa.
        group.MapPost("/login/totp", async (
            [FromBody] TotpLoginRequest req,
            EfUserStore userStore,
            IUserRepository userRepo,
            IRoleRepository roleRepo,
            ITotpChallengeTokens totpTokens,
            ITotpLoginVerifier verifier,
            HttpContext http,
            IdentityDbContext db,
            CancellationToken ct) =>
        {
            var userIdRaw = totpTokens.ValidateAndGetUserId(req.TotpToken);
            if (string.IsNullOrWhiteSpace(userIdRaw))
            {
                return Results.Json(new
                {
                    error = "totp_token_invalid_or_expired",
                    detail = totpTokens.GetLastValidationError(),
                }, statusCode: StatusCodes.Status401Unauthorized);
            }
            if (!Aethra.Shared.Kernel.Ids.AethraId.TryParse(userIdRaw, out var parsed) || parsed.Value.Prefix != "usr")
            {
                return Results.Json(new { error = "totp_token_invalid_or_expired" },
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            var uid = new UserId(parsed.Value);
            var user = await userRepo.GetByIdAsync(uid, ct);
            if (user is null || !user.IsActive)
            {
                return Results.Json(new { error = "user_not_found" }, statusCode: StatusCodes.Status401Unauthorized);
            }
            if (!user.TotpEnabled)
            {
                return Results.Json(new { error = "totp_not_enabled" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var ok = await verifier.VerifyAsync(user, req.Code, ct);
            if (!ok)
            {
                return Results.Json(new { error = "totp_invalid_code" },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            user.MarkLogin(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);

            var roleList = await roleRepo.ListByIdsAsync([.. user.Roles.Select(r => r.RoleId)], ct);
            var identity = BuildIdentityForUser(user, roleList);

            await http.SignInAsync(AuthSchemes.Cookie, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
            return Results.Ok(new
            {
                email = identity.FindFirst(ClaimTypes.Email)?.Value,
                requires_totp = false,
            });
        })
        .WithName("LoginTotp")
        .AllowAnonymous();

        // Restringido a cookie: una API key NO debe poder invalidar la sesión humana
        // (default policy es 'cookie OR apikey', así que sin esta restricción cualquier
        // owner de una key con scope mínimo podría sign-out al admin).
        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(AuthSchemes.Cookie);
            return Results.Ok(new { logged_out = true });
        })
        .WithName("Logout")
        .RequireAuthorization("CookieOnly");

        // /auth/me reporta la sesión humana (cookie). Si una API key consulta este
        // endpoint, los claims emitidos por el handler de API key no coinciden con la
        // shape esperada (Email + scope=admin) y filtraría metadata interna — por eso
        // restringimos a cookie únicamente vía la policy "CookieOnly".
        group.MapGet("/me", async (HttpContext http, IUserRepository userRepo, CancellationToken ct) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Json(new { error = "not_authenticated" }, statusCode: StatusCodes.Status401Unauthorized);
            }
            // F12.1B: reportar totp_enabled para que la UI muestre el estado en settings/security.
            bool? totpEnabled = null;
            int? recoveryRemaining = null;
            var uidRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(uidRaw)
                && Aethra.Shared.Kernel.Ids.AethraId.TryParse(uidRaw, out var parsed)
                && parsed.Value.Prefix == "usr")
            {
                var user = await userRepo.GetByIdAsync(new UserId(parsed.Value), ct);
                if (user is not null)
                {
                    totpEnabled = user.TotpEnabled;
                    recoveryRemaining = user.TotpEnabled
                        ? Aethra.Modules.Identity.Domain.Totp.RecoveryCodes.RemainingCount(user.TotpRecoveryCodesUsedMask)
                        : null;
                }
            }
            // F12.3 — exponer gitHubUsername + userId para el editor de profile del frontend.
            string? gitHubUsername = null;
            if (!string.IsNullOrWhiteSpace(uidRaw)
                && Aethra.Shared.Kernel.Ids.AethraId.TryParse(uidRaw, out var uidParsed)
                && uidParsed.Value.Prefix == "usr")
            {
                var user = await userRepo.GetByIdAsync(new UserId(uidParsed.Value), ct);
                gitHubUsername = user?.GitHubUsername;
            }

            return Results.Ok(new
            {
                userId = uidRaw,
                email = http.User.FindFirstValue(ClaimTypes.Email),
                displayName = http.User.FindFirstValue(ClaimTypes.Name),
                gitHubUsername,
                roles = http.User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(),
                scopes = http.User.FindAll(ApiKeyAuthSchemes.ScopeClaim).Select(c => c.Value).Distinct(),
                totp_enabled = totpEnabled,
                totp_recovery_codes_remaining = recoveryRemaining,
            });
        })
        .WithName("Me")
        .RequireAuthorization("CookieOnly");

        // F12.3 — PATCH /auth/me/profile: el usuario actualiza SU propio profile (GitHubUsername).
        // No usa /api/identity/users/{id} para evitar escalada de privilegios.
        group.MapPatch("/me/profile", async (
            [FromBody] UpdateMyProfileRequest body,
            HttpContext http,
            MediatR.IMediator mediator,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)
                || !Aethra.Shared.Kernel.Ids.AethraId.TryParse(userId, out var parsedUid)
                || parsedUid.Value.Prefix != "usr")
            {
                return Results.Json(new { error = "not_a_real_user" }, statusCode: StatusCodes.Status400BadRequest);
            }
            var cmd = new Aethra.Modules.Identity.UseCases.Commands.UpdateUserCommand(
                UserId: userId,
                DisplayName: null,
                RoleSlugs: null,
                GitHubUsername: body.GitHubUsername,
                ClearGitHubUsername: body.ClearGitHubUsername ?? false);
            var r = await mediator.Send(cmd, ct);
            return r.IsSuccess
                ? Results.NoContent()
                : Results.UnprocessableEntity(new { r.Error.Code, r.Error.Message });
        })
        .WithName("UpdateMyProfile")
        .RequireAuthorization("CookieOnly");

        return app;
    }

    /// <summary>F12.3 — body para <c>PATCH /auth/me/profile</c>.</summary>
    public sealed record UpdateMyProfileRequest(string? GitHubUsername, bool? ClearGitHubUsername);

    private static ClaimsIdentity BuildIdentityForUser(User user, IReadOnlyList<Role> roleList)
    {
        var aggregatedScopes = roleList
            .SelectMany(r => r.Scopes)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName ?? user.Email),
        };
        foreach (var role in roleList)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Slug));
        }
        foreach (var scope in aggregatedScopes)
        {
            claims.Add(new Claim(ApiKeyAuthSchemes.ScopeClaim, scope));
        }
        return new ClaimsIdentity(claims, AuthSchemes.Cookie);
    }
}
