using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure.Email;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Notifications.Infrastructure.Dispatch;

/// <summary>
/// BackgroundService que polea <see cref="NotificationDelivery"/> Pending cada 5s y dispara
/// segun el tipo del canal. Reintentos con backoff exponencial + jitter, max 5 attempts, despues
/// queda marcada Failed permanente.
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    INotificationConfigCodec codec,
    IClock clock,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private const int MaxAttempts = 5;
    private const int PollIntervalMs = 5_000;
    private const int BatchSize = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NotificationDispatcher arrancando (poll cada {Ms}ms)", PollIntervalMs);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "NotificationDispatcher: fallo en loop principal");
            }

            try
            {
                await Task.Delay(PollIntervalMs, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var now = clock.UtcNow;

        var pending = await db.NotificationDeliveries
            .Where(d => d.Status == NotificationDeliveryStatus.Pending
                && (d.NextAttemptAt == null || d.NextAttemptAt <= now))
            .OrderBy(d => d.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pending.Count == 0) { return; }

        var channelIds = pending.Select(d => d.ChannelId).Distinct().ToList();
        var channels = await db.NotificationChannels
            .Where(c => channelIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct)
            .ConfigureAwait(false);

        foreach (var delivery in pending)
        {
            if (!channels.TryGetValue(delivery.ChannelId, out var channel))
            {
                // Canal borrado mientras la entrega estaba pendiente: marcar failed permanente.
                delivery.MarkPermanentlyFailed("Canal no existe", clock.UtcNow);
                continue;
            }
            if (!channel.IsActive)
            {
                delivery.MarkPermanentlyFailed("Canal inactivo", clock.UtcNow);
                continue;
            }

            await TryDeliverAsync(delivery, channel, ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<(bool ok, string? error)> TryDeliverOneAsync(
        NotificationDelivery delivery, NotificationChannel channel, CancellationToken ct)
    {
        try
        {
            await DispatchByTypeAsync(channel, delivery.EventType, delivery.Payload, ct)
                .ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task TryDeliverAsync(NotificationDelivery delivery, NotificationChannel channel, CancellationToken ct)
    {
        var (ok, error) = await TryDeliverOneAsync(delivery, channel, ct).ConfigureAwait(false);
        if (ok)
        {
            delivery.MarkSent(clock.UtcNow);
            channel.MarkDelivered(clock.UtcNow);
            return;
        }

        if (delivery.Attempts + 1 >= MaxAttempts)
        {
            delivery.MarkPermanentlyFailed(error ?? "unknown", clock.UtcNow);
            logger.LogWarning(
                "NotificationDelivery {Id} failed permanente tras {Attempts} attempts: {Error}",
                delivery.Id, delivery.Attempts + 1, error);
        }
        else
        {
            var backoff = ComputeBackoff(delivery.Attempts);
            delivery.MarkAttemptFailed(error ?? "unknown", clock.UtcNow + backoff, clock.UtcNow);
            logger.LogInformation(
                "NotificationDelivery {Id} attempt {Attempt} failed, retry en {Seconds}s: {Error}",
                delivery.Id, delivery.Attempts, backoff.TotalSeconds, error);
        }
    }

    private async Task DispatchByTypeAsync(NotificationChannel channel, string eventType, string payload, CancellationToken ct)
    {
        var configJson = codec.Decode(channel.ConfigCipher);
        switch (channel.Type)
        {
            case NotificationChannelType.Slack:
                await DispatchSlackAsync(configJson, eventType, payload, ct).ConfigureAwait(false);
                break;
            case NotificationChannelType.Discord:
                await DispatchDiscordAsync(configJson, eventType, payload, ct).ConfigureAwait(false);
                break;
            case NotificationChannelType.Telegram:
                await DispatchTelegramAsync(configJson, eventType, payload, ct).ConfigureAwait(false);
                break;
            case NotificationChannelType.Webhook:
                await DispatchWebhookAsync(configJson, eventType, payload, ct).ConfigureAwait(false);
                break;
            case NotificationChannelType.Email:
                await DispatchEmailAsync(configJson, channel.Name, eventType, payload, ct).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Channel type no soportado: {channel.Type}");
        }
    }

    private async Task DispatchSlackAsync(string configJson, string eventType, string payload, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<SlackConfig>(configJson)
            ?? throw new InvalidOperationException("Config Slack invalida.");
        var text = BuildHumanText(eventType, payload);
        await PostJsonAsync(cfg.WebhookUrl, new { text }, ct).ConfigureAwait(false);
    }

    private async Task DispatchDiscordAsync(string configJson, string eventType, string payload, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<DiscordConfig>(configJson)
            ?? throw new InvalidOperationException("Config Discord invalida.");
        var text = BuildHumanText(eventType, payload);
        await PostJsonAsync(cfg.WebhookUrl, new { content = text }, ct).ConfigureAwait(false);
    }

    private async Task DispatchTelegramAsync(string configJson, string eventType, string payload, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<TelegramConfig>(configJson)
            ?? throw new InvalidOperationException("Config Telegram invalida.");
        var text = BuildHumanText(eventType, payload);
        var url = $"https://api.telegram.org/bot{cfg.BotToken}/sendMessage";
        await PostJsonAsync(url, new { chat_id = cfg.ChatId, text }, ct).ConfigureAwait(false);
    }

    private async Task DispatchWebhookAsync(string configJson, string eventType, string payload, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<WebhookConfig>(configJson)
            ?? throw new InvalidOperationException("Config Webhook invalida.");

        using var client = httpClientFactory.CreateClient("notifications");
        using var request = new HttpRequestMessage(
            new HttpMethod(string.IsNullOrWhiteSpace(cfg.HttpMethod) ? "POST" : cfg.HttpMethod),
            cfg.Url);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { event_type = eventType, payload = JsonSerializer.Deserialize<JsonElement>(payload) }),
            Encoding.UTF8, "application/json");

        if (cfg.Headers is not null)
        {
            foreach (var (k, v) in cfg.Headers)
            {
                request.Headers.TryAddWithoutValidation(k, v);
            }
        }

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Webhook respondio {(int)response.StatusCode}");
        }
    }

    private async Task DispatchEmailAsync(string configJson, string channelName, string eventType, string payload, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<EmailConfig>(configJson)
            ?? throw new InvalidOperationException("Config Email invalida.");

        var subject = $"[Aethra] {eventType}";
        var body = BuildHumanText(eventType, payload);

        // Email sender es scoped (lee IIntegrationCredentialResolver tambien scoped).
        // Como este dispatcher es singleton, abrimos scope on-demand para el envio SMTP.
        await using var scope = scopeFactory.CreateAsyncScope();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        await emailSender.SendAsync(cfg.SmtpCredentialName, cfg.From, cfg.To, subject, body, ct)
            .ConfigureAwait(false);
    }

    private async Task PostJsonAsync<TPayload>(string url, TPayload body, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"URL invalida: '{url}'.");
        }
        using var client = httpClientFactory.CreateClient("notifications");
        using var response = await client.PostAsJsonAsync(uri, body, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Endpoint respondio {(int)response.StatusCode}: {Truncate(responseBody, 200)}");
        }
    }

    private static string BuildHumanText(string eventType, string payload)
        => $"Aethra event: {eventType}\n{payload}";

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max]);

    private static TimeSpan ComputeBackoff(int attempts)
    {
        var baseSeconds = Math.Min(Math.Pow(2, attempts), 300);
        var jitter = Random.Shared.NextDouble() * 0.3 * baseSeconds;
        return TimeSpan.FromSeconds(baseSeconds + jitter);
    }

    // ---------- Config DTOs (deserializados desde el JSON cifrado, snake_case desde la UI) ----------
    private sealed record SlackConfig(
        [property: System.Text.Json.Serialization.JsonPropertyName("webhook_url")] string WebhookUrl);
    private sealed record DiscordConfig(
        [property: System.Text.Json.Serialization.JsonPropertyName("webhook_url")] string WebhookUrl);
    private sealed record TelegramConfig(
        [property: System.Text.Json.Serialization.JsonPropertyName("bot_token")] string BotToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("chat_id")] string ChatId);
    private sealed record WebhookConfig(
        [property: System.Text.Json.Serialization.JsonPropertyName("url")] string Url,
        [property: System.Text.Json.Serialization.JsonPropertyName("http_method")] string? HttpMethod,
        [property: System.Text.Json.Serialization.JsonPropertyName("headers")] Dictionary<string, string>? Headers);
    private sealed record EmailConfig(
        [property: System.Text.Json.Serialization.JsonPropertyName("smtp_credential_name")] string SmtpCredentialName,
        [property: System.Text.Json.Serialization.JsonPropertyName("from")] string From,
        [property: System.Text.Json.Serialization.JsonPropertyName("to")] string To);
}
