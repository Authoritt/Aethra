using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Aethra.Modules.Monitoring.Domain;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Monitoring.Infrastructure.Probes;

/// <summary>
/// Probe que ejecuta una request HTTP real contra <see cref="Monitor.Url"/> y clasifica
/// la respuesta. Diseño:
/// <list type="bullet">
///   <item>NO hace retries. Un Down es un Down — F6 quiere honestidad de la métrica.</item>
///   <item>Mide latencia "wall clock" con <see cref="Stopwatch"/>: incluye DNS + TCP + TLS + body.</item>
///   <item>Para Up necesitamos leer headers; el body NO se carga si el método es HEAD.</item>
///   <item>Para Degraded/Down lee hasta 200 chars del body para diagnóstico — corta el resto.</item>
/// </list>
/// </summary>
public sealed class HttpMonitorProbe(IHttpClientFactory httpClientFactory, ILogger<HttpMonitorProbe> logger)
    : IMonitorProbe
{
    public const string HttpClientName = "MonitorProbe";

    public async Task<MonitorProbeResult> ProbeAsync(Monitor monitor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var client = httpClientFactory.CreateClient(HttpClientName);
        // El timeout configurado en el monitor manda; lo aplicamos por request via CTS para no
        // depender del default del HttpClient. CTS linked respeta el ct externo del worker.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(monitor.TimeoutMs));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var request = BuildRequest(monitor);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // ResponseHeadersRead: queremos medir latencia sin descargar el body completo;
            // si lo necesitamos para snippet, lo leemos a continuación con buffer acotado.
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var latencyMs = (int)stopwatch.ElapsedMilliseconds;
            var statusCode = (int)response.StatusCode;
            var status = Classify(statusCode, monitor.ExpectedStatusCodes);

            string? snippet = null;
            if (status != MonitorStatus.Up && monitor.HttpMethod != MonitorHttpMethod.HEAD)
            {
                snippet = await ReadSnippetAsync(response.Content, linkedCts.Token).ConfigureAwait(false);
            }

            string? errorMessage = null;
            if (status == MonitorStatus.Down)
            {
                errorMessage = string.Create(
                    CultureInfo.InvariantCulture,
                    $"HTTP {statusCode} ({response.ReasonPhrase ?? "unknown"})");
            }
            else if (status == MonitorStatus.Degraded)
            {
                errorMessage = string.Create(
                    CultureInfo.InvariantCulture,
                    $"HTTP {statusCode} fuera de [{string.Join(',', monitor.ExpectedStatusCodes)}]");
            }

            return new MonitorProbeResult(status, statusCode, latencyMs, errorMessage, snippet);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Timeout interno del monitor: TaskCanceledException o OperationCanceledException ambas caen aquí.
            stopwatch.Stop();
            logger.LogDebug("Probe timeout para monitor {Slug} tras {TimeoutMs}ms",
                monitor.Slug, monitor.TimeoutMs);
            return new MonitorProbeResult(
                MonitorStatus.Down,
                HttpStatusCode: null,
                LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                ErrorMessage: string.Create(
                    CultureInfo.InvariantCulture, $"timeout tras {monitor.TimeoutMs}ms"),
                ResponseSnippet: null);
        }
        catch (OperationCanceledException)
        {
            // Cancelado por el ct externo (shutdown del worker). Propaga.
            throw;
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            return new MonitorProbeResult(
                MonitorStatus.Down,
                HttpStatusCode: null,
                LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                ErrorMessage: ex.Message,
                ResponseSnippet: null);
        }
    }

    private static HttpRequestMessage BuildRequest(Monitor monitor)
    {
        var method = monitor.HttpMethod switch
        {
            MonitorHttpMethod.HEAD => HttpMethod.Head,
            MonitorHttpMethod.POST => HttpMethod.Post,
            _ => HttpMethod.Get,
        };

        var request = new HttpRequestMessage(method, monitor.Url);

        if (monitor.Headers is { Count: > 0 } headers)
        {
            foreach (var kv in headers)
            {
                // Algunos headers son "content headers" y no pueden ir en RequestHeaders.
                if (!request.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                {
                    // Si falla aquí (ej. Content-Type sin body), lo añadimos al content si existe.
                    request.Content?.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }

        if (method == HttpMethod.Post && !string.IsNullOrEmpty(monitor.BodyTemplate))
        {
            request.Content = new StringContent(monitor.BodyTemplate, Encoding.UTF8);
            // Si el usuario no proporcionó Content-Type, asumimos JSON (sensato para webhooks de prueba).
            if (request.Content.Headers.ContentType is null)
            {
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
        }

        return request;
    }

    private static MonitorStatus Classify(int statusCode, IReadOnlyList<int> expected)
    {
        if (expected.Contains(statusCode))
        {
            return MonitorStatus.Up;
        }
        if (statusCode is >= 200 and < 300)
        {
            // Responde 2xx pero no es el esperado: degraded (servicio vivo pero "raro").
            return MonitorStatus.Degraded;
        }
        return MonitorStatus.Down;
    }

    private static async Task<string?> ReadSnippetAsync(HttpContent content, CancellationToken ct)
    {
        try
        {
            using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buffer = new byte[MonitorCheck.SnippetMaxLength * 4]; // margen UTF-8
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct)
                    .ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }
                offset += read;
            }
            if (offset == 0)
            {
                return null;
            }
            var text = Encoding.UTF8.GetString(buffer, 0, offset);
            if (text.Length > MonitorCheck.SnippetMaxLength)
            {
                text = text[..MonitorCheck.SnippetMaxLength];
            }
            return text;
        }
#pragma warning disable CA1031 // El snippet es best-effort: cualquier fallo al leer body no debe romper el probe.
        catch
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
