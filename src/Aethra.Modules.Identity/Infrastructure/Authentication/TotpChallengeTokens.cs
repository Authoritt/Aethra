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
/// La firma usa una key configurable (<see cref="IdentityOptions.TotpChallengeSigningKey"/>);
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
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private readonly byte[] _signingKey;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private string? _lastError;

    public string? GetLastValidationError() => _lastError;

    public TotpChallengeTokens(IOptions<IdentityOptions> options)
    {
        var configured = options.Value.TotpChallengeSigningKey;
        _signingKey = string.IsNullOrWhiteSpace(configured)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(configured);
        // HS256 requiere key >= 32 bytes; padeamos si quedo corta.
        if (_signingKey.Length < 32)
        {
            var padded = new byte[32];
            Buffer.BlockCopy(_signingKey, 0, padded, 0, _signingKey.Length);
            _signingKey = padded;
        }
    }

    public string Issue(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim("uid", userId) },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(DefaultLifetime),
            signingCredentials: creds);
        return _handler.WriteToken(token);
    }

    public string? ValidateAndGetUserId(string token)
    {
        _lastError = null;
        if (string.IsNullOrWhiteSpace(token)) { _lastError = "empty_token"; return null; }
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
            var principal = _handler.ValidateToken(token, parameters, out _);
            return principal.FindFirst("uid")?.Value;
        }
        catch (Exception ex)
        {
            _lastError = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }
}
