using System.Security.Claims;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Contracts.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Aethra.Api.Bootstrap;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string Email, string Password);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (
            [FromBody] LoginRequest req,
            EfUserStore userStore,
            IRoleRepository roleRepo,
            SingleUserStore singleUserStore,
            HttpContext http,
            IdentityDbContext db,
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

                // Persistir LastLoginAt — MarkLogin ya se invocó dentro del store.
                await db.SaveChangesAsync(ct);

                // Cargar roles y scopes para construir los claims.
                var roleList = await roleRepo.ListByIdsAsync([.. user.Roles.Select(r => r.RoleId)], ct);
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

                identity = new ClaimsIdentity(claims, AuthSchemes.Cookie);
            }
            else
            {
                if (!singleUserStore.ValidateCredentials(req.Email, req.Password))
                {
                    return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
                }
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

            return Results.Ok(new { email = identity.FindFirst(ClaimTypes.Email)?.Value });
        })
        .WithName("Login")
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
        group.MapGet("/me", (HttpContext http) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Json(new { error = "not_authenticated" }, statusCode: StatusCodes.Status401Unauthorized);
            }
            return Results.Ok(new
            {
                email = http.User.FindFirstValue(ClaimTypes.Email),
                displayName = http.User.FindFirstValue(ClaimTypes.Name),
                roles = http.User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(),
                scopes = http.User.FindAll(ApiKeyAuthSchemes.ScopeClaim).Select(c => c.Value).Distinct(),
            });
        })
        .WithName("Me")
        .RequireAuthorization("CookieOnly");

        return app;
    }
}
