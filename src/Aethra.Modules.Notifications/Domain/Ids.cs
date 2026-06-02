using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Notifications.Domain;

public readonly record struct NotificationChannelId(AethraId Value)
{
    public static NotificationChannelId New() => new(AethraId.NewId("nch"));
    public override string ToString() => Value.ToString();
}

public readonly record struct NotificationDeliveryId(AethraId Value)
{
    public static NotificationDeliveryId New() => new(AethraId.NewId("ndl"));
    public override string ToString() => Value.ToString();
}
