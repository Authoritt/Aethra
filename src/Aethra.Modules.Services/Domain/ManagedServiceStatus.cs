namespace Aethra.Modules.Services.Domain;

public enum ManagedServiceStatus
{
    Provisioning,
    Ready,
    Failed,
    Stopped,
}

public static class ManagedServiceStatusExtensions
{
    public static bool IsTerminal(this ManagedServiceStatus s) =>
        s is ManagedServiceStatus.Failed or ManagedServiceStatus.Stopped;
}
