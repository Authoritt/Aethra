using Microsoft.AspNetCore.Http;

namespace Aethra.Modules.Deployments.Webhooks;

internal static class GitHubWebhookBodyReader
{
    // GitHub documents webhook payloads as capped at 25 MB. We use 25 MiB to avoid
    // rejecting legitimate deliveries due to decimal/binary ambiguity while still
    // bounding anonymous pre-authentication allocations.
    // Source: https://docs.github.com/en/webhooks/webhook-events-and-payloads
    internal const int MaxBodyBytes = 25 * 1024 * 1024;

    private const int ReadBufferBytes = 16 * 1024;

    public static async Task<GitHubWebhookBodyReadResult> ReadAsync(
        HttpRequest request,
        CancellationToken ct,
        int maxBodyBytes = MaxBodyBytes)
    {
        if (request.ContentLength is > 0 && request.ContentLength > maxBodyBytes)
        {
            return GitHubWebhookBodyReadResult.PayloadTooLarge;
        }

        var declaredLength = request.ContentLength;
        if (declaredLength is >= 0)
        {
            return await ReadDeclaredLengthAsync(request.Body, (int)declaredLength.Value, ct).ConfigureAwait(false);
        }

        return await ReadUnknownLengthAsync(request.Body, maxBodyBytes, ct).ConfigureAwait(false);
    }

    private static async Task<GitHubWebhookBodyReadResult> ReadDeclaredLengthAsync(
        Stream bodyStream,
        int declaredLength,
        CancellationToken ct)
    {
        var body = new byte[declaredLength];
        var offset = 0;
        while (offset < declaredLength)
        {
            var read = await bodyStream
                .ReadAsync(body.AsMemory(offset, declaredLength - offset), ct)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        if (offset != declaredLength)
        {
            Array.Resize(ref body, offset);
        }

        return GitHubWebhookBodyReadResult.Accepted(body);
    }

    private static async Task<GitHubWebhookBodyReadResult> ReadUnknownLengthAsync(
        Stream bodyStream,
        int maxBodyBytes,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[Math.Min(ReadBufferBytes, Math.Max(1, maxBodyBytes + 1))];

        while (true)
        {
            var read = await bodyStream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return GitHubWebhookBodyReadResult.Accepted(ms.ToArray());
            }

            if (ms.Length + read > maxBodyBytes)
            {
                return GitHubWebhookBodyReadResult.PayloadTooLarge;
            }

            ms.Write(buffer, 0, read);
        }
    }
}

internal sealed record GitHubWebhookBodyReadResult(bool IsPayloadTooLarge, byte[] Body)
{
    public static GitHubWebhookBodyReadResult PayloadTooLarge { get; } = new(true, []);

    public static GitHubWebhookBodyReadResult Accepted(byte[] body) => new(false, body);
}
