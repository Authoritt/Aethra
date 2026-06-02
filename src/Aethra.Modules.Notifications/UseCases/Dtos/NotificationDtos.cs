using System.Text.Json;

namespace Aethra.Modules.Notifications.UseCases.Dtos;

public sealed record NotificationChannelDto(
    string Id,
    string Name,
    string Type,
    bool IsActive,
    IReadOnlyList<string> EventFilters,
    JsonElement? Config,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastDeliveredAt);

public sealed record NotificationDeliveryDto(
    string Id,
    string ChannelId,
    string ChannelName,
    string EventType,
    string Status,
    int Attempts,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

public sealed record TestChannelResultDto(
    bool Success,
    string? Error,
    DateTimeOffset AttemptedAt);
