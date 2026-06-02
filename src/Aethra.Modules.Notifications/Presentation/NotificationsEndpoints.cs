using System.Text.Json;
using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.UseCases.Commands;
using Aethra.Modules.Notifications.UseCases.Queries;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Aethra.Modules.Notifications.Presentation;

public static class NotificationsEndpoints
{
    private const string ScopeRead = "scope:notifications:read";
    private const string ScopeWrite = "scope:notifications:write";

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var channels = app.MapGroup("/api/notifications/channels").WithTags("Notifications");

        channels.MapGet("/", async (IMediator m, CancellationToken ct) =>
            ToResult(await m.Send(new ListChannelsQuery(), ct)))
            .RequireAuthorization(ScopeRead)
            .WithName("ListNotificationChannels");

        channels.MapPost("/", async ([FromBody] CreateChannelRequest body, IMediator m, CancellationToken ct) =>
        {
            if (!Enum.TryParse<NotificationChannelType>(body.Type, ignoreCase: true, out var type))
            {
                return Results.UnprocessableEntity(new
                {
                    code = "channel.invalid_type",
                    message = $"Tipo invalido: '{body.Type}'. Use Slack|Discord|Telegram|Email|Webhook.",
                });
            }
            var cmd = new CreateChannelCommand(body.Name, type, body.Config, body.EventFilters);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess
                ? Results.Created($"/api/notifications/channels/{r.Value.Id}", r.Value)
                : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("CreateNotificationChannel");

        channels.MapPatch("/{channelId}", async (string channelId, [FromBody] PatchChannelRequest body,
            IMediator m, CancellationToken ct) =>
        {
            var cmd = new PatchChannelCommand(channelId, body.IsActive, body.Config, body.EventFilters);
            var r = await m.Send(cmd, ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("PatchNotificationChannel");

        channels.MapDelete("/{channelId}", async (string channelId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new DeleteChannelCommand(channelId), ct);
            return r.IsSuccess ? Results.NoContent() : MapError(r.Error);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("DeleteNotificationChannel");

        channels.MapPost("/{channelId}/test", async (string channelId, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new TestChannelCommand(channelId), ct);
            return ToResult(r);
        })
        .RequireAuthorization(ScopeWrite)
        .WithName("TestNotificationChannel");

        var deliveries = app.MapGroup("/api/notifications/deliveries").WithTags("Notifications");
        deliveries.MapGet("/", async (
            [FromQuery(Name = "channel_id")] string? channelId,
            [FromQuery] string? status,
            [FromQuery] int? limit,
            IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new ListDeliveriesQuery(channelId, status, limit ?? 50), ct);
            return ToResult(r);
        })
        .RequireAuthorization(ScopeRead)
        .WithName("ListNotificationDeliveries");

        return app;
    }

    public sealed record CreateChannelRequest(
        string Name,
        string Type,
        JsonElement Config,
        IReadOnlyList<string>? EventFilters);

    public sealed record PatchChannelRequest(
        bool? IsActive,
        JsonElement? Config,
        IReadOnlyList<string>? EventFilters);

    private static IResult ToResult<T>(Result<T> r)
        => r.IsSuccess ? Results.Ok(r.Value) : MapError(r.Error);

    private static IResult MapError(Error e) => e.Type switch
    {
        ErrorType.Validation => Results.UnprocessableEntity(new { code = e.Code, message = e.Message }),
        ErrorType.NotFound => Results.NotFound(new { code = e.Code, message = e.Message }),
        ErrorType.Conflict => Results.Conflict(new { code = e.Code, message = e.Message }),
        _ => Results.Problem(e.Message),
    };
}
