using System.Globalization;

namespace Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;

/// <summary>
/// Excepcion lanzada cuando el API v4 de Cloudflare devuelve un error de negocio
/// (HTTP no-2xx o JSON con <c>success: false</c>). El <see cref="Code"/> corresponde al
/// codigo numerico de Cloudflare; <see cref="StatusCode"/> al HTTP. La excepcion es
/// capturada por los handlers de CQRS y mapeada a <c>Error</c>.
/// </summary>
public sealed class CloudflareApiException : Exception
{
    public int StatusCode { get; }
    public int Code { get; }

    public CloudflareApiException(int statusCode, int code, string message)
        : base(BuildMessage(statusCode, code, message))
    {
        StatusCode = statusCode;
        Code = code;
    }

    public CloudflareApiException(int statusCode, int code, string message, Exception inner)
        : base(BuildMessage(statusCode, code, message), inner)
    {
        StatusCode = statusCode;
        Code = code;
    }

    private static string BuildMessage(int statusCode, int code, string message)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"Cloudflare API error (HTTP {statusCode}, code {code}): {message}");
}
