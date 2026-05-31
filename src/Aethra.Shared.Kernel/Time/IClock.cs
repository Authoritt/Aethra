namespace Aethra.Shared.Kernel.Time;

/// <summary>
/// Abstracción del tiempo. Usar en lugar de <see cref="DateTimeOffset.UtcNow"/> en código
/// que requiera ser testeable (tiempo controlable en tests).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
