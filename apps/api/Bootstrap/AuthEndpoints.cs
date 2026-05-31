using System.Security.Claims;
using Aethra.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
            SingleUserStore store,
            HttpContext http) =>
        {
            if (!store.ValidateCredentials(req.Email, req.Password))
            {
                return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Email, store.AdminEmail),
                new Claim(ClaimTypes.Name, store.AdminEmail),
                new Claim("scope", "admin"),
            ], AuthSchemes.Cookie);

            await http.SignInAsync(AuthSchemes.Cookie, new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                IsPersistent = true,
            });

            return Results.Ok(new { email = store.AdminEmail });
        })
        .WithName("Login")
        .AllowAnonymous();

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(AuthSchemes.Cookie);
            return Results.Ok(new { logged_out = true });
        })
        .WithName("Logout");

        group.MapGet("/me", (HttpContext http) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Results.Json(new { error = "not_authenticated" }, statusCode: StatusCodes.Status401Unauthorized);
            }
            return Results.Ok(new
            {
                email = http.User.FindFirstValue(ClaimTypes.Email),
                scopes = http.User.FindAll("scope").Select(c => c.Value),
            });
        })
        .WithName("Me");

        return app;
    }
}
