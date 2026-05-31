using System.Security.Cryptography;
using System.Text;

namespace Aethra.Modules.Deployments.Webhooks;

/// <summary>
/// Valida el header <c>X-Hub-Signature-256</c> que GitHub envía con cada webhook.
/// Formato del header: <c>sha256=&lt;hex hmac&gt;</c>. Comparación constant-time.
/// </summary>
public static class GitHubSignatureValidator
{
    public static bool Validate(string? presentedSignatureHeader, byte[] body, string sharedSecret)
    {
        if (string.IsNullOrWhiteSpace(presentedSignatureHeader) || string.IsNullOrWhiteSpace(sharedSecret))
        {
            return false;
        }
        const string prefix = "sha256=";
        if (!presentedSignatureHeader.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }
        var presentedHex = presentedSignatureHeader[prefix.Length..];
        var expectedHex = ComputeHmacHex(body, sharedSecret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(presentedHex.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(expectedHex));
    }

    private static string ComputeHmacHex(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(body);
        return Convert.ToHexStringLower(hash);
    }
}
