using Aethra.Shared.Kernel.Ids;

namespace Aethra.Modules.Services.Domain;

public readonly record struct ManagedServiceId(AethraId Value)
{
    public static ManagedServiceId New() => new(AethraId.NewId("svc"));
    public override string ToString() => Value.ToString();
}

public readonly record struct ServiceBindingId(AethraId Value)
{
    public static ServiceBindingId New() => new(AethraId.NewId("bnd"));
    public override string ToString() => Value.ToString();
}

public readonly record struct ServiceBackupId(AethraId Value)
{
    public static ServiceBackupId New() => new(AethraId.NewId("bkp"));
    public override string ToString() => Value.ToString();
}

public readonly record struct ScheduledJobId(AethraId Value)
{
    public static ScheduledJobId New() => new(AethraId.NewId("sch"));
    public override string ToString() => Value.ToString();
}

public readonly record struct ScheduledJobRunId(AethraId Value)
{
    public static ScheduledJobRunId New() => new(AethraId.NewId("schr"));
    public override string ToString() => Value.ToString();
}
