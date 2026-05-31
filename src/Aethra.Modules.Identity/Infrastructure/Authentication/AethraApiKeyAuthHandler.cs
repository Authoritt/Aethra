using System.Security.Claims;
using System.Text.Encodings.Web;
using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure.Persistence;
using Aethra.Shared.Kernel.Time;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

public sealed class AethraApiKeyAuthOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Handler que valida el header <c>Authorization: Bearer aethra_...</c>, busca el hash
/// determinístico en la BD, y emite un <see cref="ClaimsPrincipal"/> con un claim
/// por scope. Si la key está revocada o expirada, devuelve Fail.
///
/// El <c>LastUsedAt</c> se actualiza en una operación fire-and-forget que crea su
/// propio scope para no bloquear la request — si la BD está lenta, la auth no se
/// degrada. Si la actualización falla, se loguea sin propagar.
/// </summary>
public sealed class AethraApiKeyAuthHandler(
    IOptionsMonitor<AethraApiKeyAuthOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyHasher hasher,
    IApiKeyRepository repository,
    IClock clock,
    IServiceScopeFactory scopeFactory)
    : AuthenticationHandler<AethraApiKeyAuthOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ResolveToken();
        if (string.IsNullOrEmpty(token))
        {
            // NoResult permite que otros schemes (cookie) intenten autenticar la request.
            return AuthenticateResult.NoResult();
        }

        if (!token.StartsWith(ApiKeyGenerator.SecretPrefix, StringComparison.Ordinal))
        {
            // Bearer presente pero no es nuestro formato — dejamos pasar a otros handlers.
            return AuthenticateResult.NoResult();
        }

        byte[] hash;
        try
        {
            hash = hasher.Hash(token);
        }
        catch (ArgumentException)
        {
            return AuthenticateResult.Fail("Invalid API key");
        }

        var apiKey = await repository.FindByHashAsync(hash, Context.RequestAborted).ConfigureAwait(false);
        if (apiKey is null)
        {
            Logger.LogWarning("API key inválida presentada desde {Ip}", Context.Connection.RemoteIpAddress);
            return AuthenticateResult.Fail("Invalid API key");
        }

        var now = clock.UtcNow;
        if (!apiKey.IsActive(now))
        {
            Logger.LogWarning("API key {Id} presentada pero está revocada/expirada", apiKey.Id);
            return AuthenticateResult.Fail("API key revoked or expired");
        }

        var identity = new ClaimsIdentity(BuildClaims(apiKey), Scheme.Name);

        // Fire-and-forget: actualizar LastUsedAt fuera del hot path. Crea su propio scope
        // porque el DbContext es scoped al request actual y puede haber sido despuesto antes
        // de que la tarea termine.
        _ = MarkUsedAsync(apiKey.Id, now);

        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity), Scheme.Name));
    }

    private string? ResolveToken()
    {
        if (!Context.Request.Headers.TryGetValue(ApiKeyAuthSchemes.AuthorizationHeader, out var header))
        {
            return null;
        }
        var value = header.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return value.StartsWith(ApiKeyAuthSchemes.BearerPrefix, StringComparison.Ordinal)
            ? value[ApiKeyAuthSchemes.BearerPrefix.Length..].Trim()
            : null;
    }

    private static IEnumerable<Claim> BuildClaims(ApiKey apiKey)
    {
        var id = apiKey.Id.ToString();
        yield return new Claim(ClaimTypes.NameIdentifier, id);
        yield return new Claim(ApiKeyAuthSchemes.ApiKeyIdClaim, id);
        yield return new Claim(ClaimTypes.Name, apiKey.Name);
        foreach (var scope in apiKey.Scopes)
        {
            yield return new Claim(ApiKeyAuthSchemes.ScopeClaim, scope);
        }
    }

    private async Task MarkUsedAsync(ApiKeyId id, DateTimeOffset now)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tracked = await db.ApiKeys
                .FirstOrDefaultAsync(k => k.Id == id, CancellationToken.None)
                .ConfigureAwait(false);
            if (tracked is null)
            {
                return;
            }
            tracked.MarkUsed(now);
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // No propagamos: la auth ya tuvo éxito; perder un MarkUsed solo afecta
            // a la columna last_used_at.
            Logger.LogWarning(ex, "No se pudo actualizar LastUsedAt para API key {Id}", id);
        }
    }
}
