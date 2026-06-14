using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.UseCases.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Notifications.Tests;

/// <summary>
/// <see cref="NotificationConfigShape.Validate"/> — claves requeridas por tipo de canal. Compartido por
/// CreateChannel y PatchChannel (este último antes no revalidaba → config rota en silencio).
/// </summary>
public sealed class NotificationConfigShapeTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public void Slack_accepts_webhook_url()
        => NotificationConfigShape.Validate(NotificationChannelType.Slack, Json("""{"webhook_url":"https://hooks.slack.com/x"}"""))
            .Should().BeNull();

    [Fact]
    public void Slack_rejects_missing_webhook_url()
        => NotificationConfigShape.Validate(NotificationChannelType.Slack, Json("{}")).Should().NotBeNull();

    [Fact]
    public void Telegram_requires_bot_token_and_chat_id()
    {
        NotificationConfigShape.Validate(NotificationChannelType.Telegram, Json("""{"bot_token":"t","chat_id":"-100"}"""))
            .Should().BeNull();
        NotificationConfigShape.Validate(NotificationChannelType.Telegram, Json("""{"bot_token":"t"}"""))
            .Should().NotBeNull();
    }

    [Fact]
    public void Email_requires_three_keys()
    {
        NotificationConfigShape.Validate(NotificationChannelType.Email,
            Json("""{"smtp_credential_name":"c","from":"a@b.com","to":"c@d.com"}""")).Should().BeNull();
        NotificationConfigShape.Validate(NotificationChannelType.Email, Json("""{"from":"a@b.com","to":"c@d.com"}"""))
            .Should().NotBeNull();
    }

    [Fact]
    public void Webhook_requires_url()
    {
        NotificationConfigShape.Validate(NotificationChannelType.Webhook, Json("""{"url":"https://x/y"}""")).Should().BeNull();
        NotificationConfigShape.Validate(NotificationChannelType.Webhook, Json("{}")).Should().NotBeNull();
    }

    [Fact]
    public void Rejects_non_object_config()
        => NotificationConfigShape.Validate(NotificationChannelType.Slack, Json("\"not-an-object\"")).Should().NotBeNull();

    [Fact]
    public void Rejects_empty_string_value()
        => NotificationConfigShape.Validate(NotificationChannelType.Slack, Json("""{"webhook_url":"  "}""")).Should().NotBeNull();
}
