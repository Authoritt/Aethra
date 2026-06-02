using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure;
using Aethra.Modules.Notifications.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Notifications.UseCases.Queries;

public sealed record ListDeliveriesQuery(string? ChannelId, string? Status, int Limit)
    : IQuery<IReadOnlyList<NotificationDeliveryDto>>;

internal sealed class ListDeliveriesHandler(NotificationsDbContext db)
    : IQueryHandler<ListDeliveriesQuery, IReadOnlyList<NotificationDeliveryDto>>
{
    public async Task<Result<IReadOnlyList<NotificationDeliveryDto>>> Handle(ListDeliveriesQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 500);

        var query = db.NotificationDeliveries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.ChannelId)
            && AethraId.TryParse(request.ChannelId, out var parsed)
            && parsed.Value.Prefix == "nch")
        {
            var cid = new NotificationChannelId(parsed.Value);
            query = query.Where(d => d.ChannelId == cid);
        }
        if (Enum.TryParse<NotificationDeliveryStatus>(request.Status, ignoreCase: true, out var status))
        {
            query = query.Where(d => d.Status == status);
        }

        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var channelIds = rows.Select(r => r.ChannelId).Distinct().ToList();
        var channels = await db.NotificationChannels
            .AsNoTracking()
            .Where(c => channelIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<NotificationDeliveryDto> result = rows.Select(r => new NotificationDeliveryDto(
            r.Id.ToString(),
            r.ChannelId.ToString(),
            channels.TryGetValue(r.ChannelId, out var name) ? name : "(deleted)",
            r.EventType,
            r.Status.ToString(),
            r.Attempts,
            r.Error,
            r.CreatedAt,
            r.SentAt)).ToList();

        return Result<IReadOnlyList<NotificationDeliveryDto>>.Success(result);
    }
}
