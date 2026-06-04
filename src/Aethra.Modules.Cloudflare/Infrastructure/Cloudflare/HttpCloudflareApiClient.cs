using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aethra.Modules.Cloudflare.Infrastructure.Cloudflare;

/// <summary>
/// Implementacion HTTP de <see cref="ICloudflareApiClient"/> contra el API v4.
/// El <see cref="HttpClient"/> es el named-client <c>"Cloudflare"</c> con BaseAddress
/// <c>https://api.cloudflare.com/client/v4/</c>. El token se pasa por request
/// (no por DefaultRequestHeaders) porque cada zona puede tener su propio token.
/// </summary>
public sealed class HttpCloudflareApiClient : ICloudflareApiClient
{
    public const string HttpClientName = "Cloudflare";

    /// <summary>BaseAddress por defecto del API v4 de Cloudflare.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://api.cloudflare.com/client/v4/", UriKind.Absolute);

    // Cacheado para evitar CA1869 (re-instanciar JsonSerializerOptions es caro).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    public HttpCloudflareApiClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.BaseAddress is null)
        {
            http.BaseAddress = DefaultBaseAddress;
        }
        _http = http;
    }

    public async Task<CloudflareZoneInfo> GetZoneAsync(string zoneId, string apiToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);

        using var request = BuildRequest(HttpMethod.Get, $"zones/{Uri.EscapeDataString(zoneId)}", apiToken);
        var envelope = await SendAsync<ZoneJson>(request, cancellationToken).ConfigureAwait(false);
        var z = envelope.Result
            ?? throw new CloudflareApiException(200, 0, "Respuesta de Cloudflare sin 'result' para get zone.");

        var account = z.Account?.Id ?? string.Empty;
        return new CloudflareZoneInfo(
            Id: z.Id ?? zoneId,
            Name: z.Name ?? string.Empty,
            Status: z.Status ?? "unknown",
            AccountId: account);
    }

    public async Task<IReadOnlyList<CloudflareDnsRecordInfo>> ListDnsRecordsAsync(
        string zoneId,
        string apiToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);

        var records = new List<CloudflareDnsRecordInfo>();
        var page = 1;
        const int PerPage = 100;
        while (true)
        {
            var uri = string.Create(
                CultureInfo.InvariantCulture,
                $"zones/{Uri.EscapeDataString(zoneId)}/dns_records?per_page={PerPage}&page={page}");
            using var request = BuildRequest(HttpMethod.Get, uri, apiToken);
            var envelope = await SendAsync<List<DnsRecordJson>>(request, cancellationToken).ConfigureAwait(false);
            if (envelope.Result is null)
            {
                break;
            }
            foreach (var r in envelope.Result)
            {
                records.Add(new CloudflareDnsRecordInfo(
                    Id: r.Id ?? string.Empty,
                    Type: r.Type ?? string.Empty,
                    Name: r.Name ?? string.Empty,
                    Content: r.Content ?? string.Empty,
                    Ttl: r.Ttl,
                    Proxied: r.Proxied,
                    Comment: r.Comment));
            }
            var totalPages = envelope.ResultInfo?.TotalPages ?? 1;
            if (page >= totalPages || envelope.Result.Count == 0)
            {
                break;
            }
            page++;
        }
        return records;
    }

    public async Task<string> CreateDnsRecordAsync(
        string zoneId,
        string apiToken,
        CreateDnsRecordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);
        ArgumentNullException.ThrowIfNull(request);

        var payload = new DnsRecordWritePayload(request.Type, request.Name, request.Content, request.Ttl, request.Proxied, request.Comment);
        using var http = BuildRequest(HttpMethod.Post, $"zones/{Uri.EscapeDataString(zoneId)}/dns_records", apiToken);
        http.Content = JsonContent.Create(payload, options: JsonOptions);
        var envelope = await SendAsync<DnsRecordJson>(http, cancellationToken).ConfigureAwait(false);
        var id = envelope.Result?.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new CloudflareApiException(200, 0, "Cloudflare devolvio el record creado sin 'id'.");
        }
        return id;
    }

    public async Task UpdateDnsRecordAsync(
        string zoneId,
        string externalRecordId,
        string apiToken,
        UpdateDnsRecordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalRecordId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);
        ArgumentNullException.ThrowIfNull(request);

        var payload = new DnsRecordWritePayload(request.Type, request.Name, request.Content, request.Ttl, request.Proxied, request.Comment);
        var uri = string.Create(
            CultureInfo.InvariantCulture,
            $"zones/{Uri.EscapeDataString(zoneId)}/dns_records/{Uri.EscapeDataString(externalRecordId)}");
        using var http = BuildRequest(HttpMethod.Put, uri, apiToken);
        http.Content = JsonContent.Create(payload, options: JsonOptions);
        _ = await SendAsync<DnsRecordJson>(http, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteDnsRecordAsync(
        string zoneId,
        string externalRecordId,
        string apiToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalRecordId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);

        var uri = string.Create(
            CultureInfo.InvariantCulture,
            $"zones/{Uri.EscapeDataString(zoneId)}/dns_records/{Uri.EscapeDataString(externalRecordId)}");
        using var request = BuildRequest(HttpMethod.Delete, uri, apiToken);
        _ = await SendAsync<DnsRecordDeleteResultJson>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TunnelIngressRule>> GetTunnelIngressAsync(
        string accountId, string tunnelId, string apiToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tunnelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);

        var uri = $"accounts/{Uri.EscapeDataString(accountId)}/cfd_tunnel/{Uri.EscapeDataString(tunnelId)}/configurations";
        using var request = BuildRequest(HttpMethod.Get, uri, apiToken);
        var envelope = await SendAsync<TunnelConfigJson>(request, cancellationToken).ConfigureAwait(false);
        var ingress = envelope.Result?.Config?.Ingress;
        if (ingress is null)
        {
            return [];
        }
        return ingress
            .Select(r => new TunnelIngressRule(
                string.IsNullOrWhiteSpace(r.Hostname) ? null : r.Hostname,
                r.Service ?? string.Empty,
                r.OriginRequest?.NoTlsVerify ?? false))
            .ToList();
    }

    public async Task PutTunnelIngressAsync(
        string accountId, string tunnelId, string apiToken,
        IReadOnlyList<TunnelIngressRule> ingress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tunnelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);
        ArgumentNullException.ThrowIfNull(ingress);

        var rules = ingress.Select(r => new IngressRuleJson
        {
            Hostname = r.Hostname,
            Service = r.Service,
            OriginRequest = r.NoTlsVerify ? new OriginRequestJson { NoTlsVerify = true } : null,
        }).ToList();
        var payload = new TunnelConfigWriteJson { Config = new TunnelConfigBodyJson { Ingress = rules } };

        var uri = $"accounts/{Uri.EscapeDataString(accountId)}/cfd_tunnel/{Uri.EscapeDataString(tunnelId)}/configurations";
        using var http = BuildRequest(HttpMethod.Put, uri, apiToken);
        http.Content = JsonContent.Create(payload, options: JsonOptions);
        _ = await SendAsync<TunnelConfigJson>(http, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string relativeUri, string apiToken)
    {
        var request = new HttpRequestMessage(method, new Uri(relativeUri, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        return request;
    }

    private async Task<CloudflareEnvelope<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new CloudflareApiException(0, 0, "Fallo de conexion al API de Cloudflare.", ex);
        }

        using (response)
        {
            CloudflareEnvelope<T>? envelope = null;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<CloudflareEnvelope<T>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new CloudflareApiException((int)response.StatusCode, 0, "Respuesta JSON ilegible.", ex);
            }
            catch (NotSupportedException ex)
            {
                throw new CloudflareApiException((int)response.StatusCode, 0, "Content-Type no soportado.", ex);
            }

            if (envelope is null)
            {
                throw new CloudflareApiException((int)response.StatusCode, 0, "Respuesta vacia del API.");
            }
            if (!response.IsSuccessStatusCode || !envelope.Success)
            {
                var firstError = envelope.Errors?.Count > 0 ? envelope.Errors[0] : null;
                var code = firstError?.Code ?? 0;
                var message = firstError?.Message ?? response.ReasonPhrase ?? "Error desconocido.";
                throw new CloudflareApiException((int)response.StatusCode, code, message);
            }
            return envelope;
        }
    }

    // ---------------------------------------------------------------------
    // DTOs internos para deserializacion. Solo usados aqui.
    // ---------------------------------------------------------------------

    private sealed class CloudflareEnvelope<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("result")] public T? Result { get; set; }
        [JsonPropertyName("errors")] public List<CloudflareErrorJson>? Errors { get; set; }
        [JsonPropertyName("messages")] public List<CloudflareErrorJson>? Messages { get; set; }
        [JsonPropertyName("result_info")] public ResultInfoJson? ResultInfo { get; set; }
    }

    private sealed class CloudflareErrorJson
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    private sealed class ResultInfoJson
    {
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("per_page")] public int PerPage { get; set; }
        [JsonPropertyName("total_count")] public int TotalCount { get; set; }
        [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    }

    private sealed class ZoneJson
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("account")] public AccountJson? Account { get; set; }
    }

    private sealed class AccountJson
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class DnsRecordJson
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("ttl")] public int Ttl { get; set; }
        [JsonPropertyName("proxied")] public bool Proxied { get; set; }
        [JsonPropertyName("comment")] public string? Comment { get; set; }
    }

    private sealed class DnsRecordDeleteResultJson
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }

    private sealed class TunnelConfigJson
    {
        [JsonPropertyName("config")] public TunnelConfigBodyJson? Config { get; set; }
    }

    private sealed class TunnelConfigBodyJson
    {
        [JsonPropertyName("ingress")] public List<IngressRuleJson>? Ingress { get; set; }
    }

    private sealed class IngressRuleJson
    {
        [JsonPropertyName("hostname")] public string? Hostname { get; set; }
        [JsonPropertyName("service")] public string? Service { get; set; }
        [JsonPropertyName("originRequest")] public OriginRequestJson? OriginRequest { get; set; }
    }

    private sealed class OriginRequestJson
    {
        [JsonPropertyName("noTLSVerify")] public bool NoTlsVerify { get; set; }
    }

    private sealed class TunnelConfigWriteJson
    {
        [JsonPropertyName("config")] public TunnelConfigBodyJson? Config { get; set; }
    }

    private sealed record DnsRecordWritePayload(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("ttl")] int Ttl,
        [property: JsonPropertyName("proxied")] bool Proxied,
        [property: JsonPropertyName("comment")] string? Comment);
}
