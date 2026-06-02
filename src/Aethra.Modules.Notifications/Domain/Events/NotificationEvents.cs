using Aethra.Shared.Kernel.Domain;

namespace Aethra.Modules.Notifications.Domain.Events;

public sealed record NotificationChannelCreatedEvent(NotificationChannelId ChannelId, string Name, NotificationChannelType Type) : DomainEvent;

public sealed record NotificationChannelDeletedEvent(NotificationChannelId ChannelId, string Name) : DomainEvent;
