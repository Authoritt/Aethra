using System.Text.RegularExpressions;

namespace Aethra.Shared.Kernel.Security;

/// <summary>
/// Removes credentials from Git URLs before they are written to logs or operator-visible errors.
/// </summary>
public static partial class GitUrlRedactor
{
    /// <summary>
    /// Redacts URL user-info and, when supplied, every raw or URL-encoded occurrence of a secret.
    /// SSH URLs that contain only the conventional user name (for example <c>ssh://git@host</c>)
    /// are left intact because they do not contain a password or token.
    /// </summary>
    public static string Redact(string text, string? sensitiveValue = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!string.IsNullOrEmpty(sensitiveValue))
        {
            text = text
                .Replace(sensitiveValue, "***", StringComparison.Ordinal)
                .Replace(Uri.EscapeDataString(sensitiveValue), "***", StringComparison.Ordinal);
        }

        return CredentialedGitUrlRegex().Replace(text, static match =>
            $"{match.Groups["scheme"].Value}://***@");
    }

    [GeneratedRegex(
        @"(?<scheme>https?)://[^\s/?#]+@|(?<scheme>ssh)://[^\s/?#]*:[^\s/?#]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialedGitUrlRegex();
}
