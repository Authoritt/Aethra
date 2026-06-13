using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.UseCases.Commands;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Notifications.Tests;

/// <summary>
/// Tests del validator <c>CreateChannelValidator</c>: el nombre del canal es required y limitado
/// a 100 chars (el Config/EventFilters se validan en handler/dominio, no aquí). Puro, sin BD.
/// </summary>
public sealed class NotificationsValidatorTests
{
    // Clone() para que el JsonElement sobreviva al JsonDocument temporal.
    private static readonly JsonElement Config = JsonDocument.Parse("{}").RootElement.Clone();

    private static CreateChannelCommand New(string name = "Ops")
        => new(name, NotificationChannelType.Slack, Config, null);

    [Fact]
    public void CreateChannel_accepts_a_valid_command()
        => new CreateChannelValidator().Validate(New()).IsValid.Should().BeTrue();

    [Fact]
    public void CreateChannel_requires_a_name()
        => new CreateChannelValidator().Validate(New(name: "")).IsValid.Should().BeFalse();

    [Fact]
    public void CreateChannel_rejects_name_over_100_chars()
        => new CreateChannelValidator().Validate(New(name: new string('n', 101))).IsValid.Should().BeFalse();
}
