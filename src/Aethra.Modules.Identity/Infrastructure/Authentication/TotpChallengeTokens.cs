using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// F12.1B — emite y valida JWTs cortos (15 min) usados entre el step 1 (password OK) y el
/// step 2 (TOTP code OK) del login con 2FA habilitado. El claim principal es <c>uid</c>
/// (UserId). Si el token caduca o no se firma con la misma key del proceso, la verificacion
/// falla y el cliente debe reiniciar el login.
///
/// Cada JWT contiene un <c>jti</c> aleatorio cuyo estado se persiste. El challenge caduca a los
/// 15 minutos y admite como maximo 5 codigos fallidos antes de quedar bloqueado. La firma usa una key
/// configurable (<see cref="IdentityOptions.TotpChallengeSigningKey"/>);
/// si no se setea, se genera en memoria al arranque (tokens emitidos antes de un restart se
/// invalidan — aceptable por TTL corto).
/// </summary>
public interface ITotpChallengeTokens
{
    string Issue(string userId);
    string? ValidateAndGetUserId(string token);

    /// <summary>Para debugging — devuelve la razon del fallo si la validacion falla.</summary>
    string? GetLastValidationError();
}

internal sealed class TotpChallengeTokens : ITotpChallengeTokens
{
    private const string Issuer = "aethra-auth";
    private const string Audience = "aethra-totp-challenge";
    private readonly byte[] _signingKey;
    private readonly TotpChallengeStore _challenges;
    private readonly Aethra.Shared.Kernel.Time.IClock _clock;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private string? _lastError;

    public string? GetLastValidationError() => _lastError;

    public TotpChallengeTokens(
        TotpChallengeSigningKey signingKey,
        TotpChallengeStore challenges,
        Aethra.Shared.Kernel.Time.IClock clock)
    {
        _signingKey = signingKey.Value;
        _challenges = challenges;
        _clock = clock;
    }

    public string Issue(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var challengeId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var now = _clock.UtcNow;
        _challenges.Create(challengeId, userId);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim("uid", userId),
                new Claim(JwtRegisteredClaimNames.Jti, challengeId),
            ],
            notBefore: now.UtcDateTime,
            expires: now.Add(Aethra.Modules.Identity.Domain.Totp.TotpChallenge.Lifetime).UtcDateTime,
            signingCredentials: creds);
        return _handler.WriteToken(token);
    }

    public string? ValidateAndGetUserId(string token)
    {
        _lastError = null;
        _challenges.ClearCurrent();
        if (string.IsNullOrWhiteSpace(token)) { _lastError = "empty_token"; return null; }
        ClaimsPrincipal principal;
        try
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(_signingKey),
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            principal = _handler.ValidateToken(token, parameters, out _);
        }
        // ArgumentException cubre SecurityTokenMalformedException y el token mayor que
        // MaximumTokenSizeInBytes; es seguro aqui porque la consulta al almacen va fuera del try.
        catch (ArgumentException)
        {
            _lastError = "invalid_token";
            return null;
        }
        catch (SecurityTokenException)
        {
            _lastError = "invalid_token";
            return null;
        }

        var userId = principal.FindFirst("uid")?.Value;
        var challengeId = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(challengeId))
        {
            _lastError = "missing_challenge_claims";
            return null;
        }

        // Deliberately outside the JWT-validation catch: store failures are infrastructure
        // errors and must reach the API exception pipeline instead of becoming a detailed 401.
        if (!_challenges.TrySelectActive(challengeId, userId))
        {
            _lastError = "challenge_consumed_expired_or_attempts_exhausted";
            return null;
        }
        return userId;
    }
}

/// <summary>
/// Conserva una unica key generada por proceso cuando no hay configuracion. El servicio de tokens
/// es scoped porque usa EF, pero la key debe seguir siendo estable durante toda la vida del proceso.
/// En despliegues multi-replica se configura la misma key en todas las instancias.
/// </summary>
internal sealed class TotpChallengeSigningKey
{
    public byte[] Value { get; }

    public TotpChallengeSigningKey(IOptions<IdentityOptions> options)
    {
        var configured = options.Value.TotpChallengeSigningKey;
        var value = string.IsNullOrWhiteSpace(configured)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(configured);
        if (value.Length < 32)
        {
            Array.Resize(ref value, 32);
        }
        Value = value;
    }
}
