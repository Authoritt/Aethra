namespace Aethra.Satellite;

public sealed class SatelliteOptions
{
    public string CentralUrl { get; set; } = "http://localhost:5080";
    public string Token { get; set; } = string.Empty;
    public int MetricsIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Path al archivo SQLite del buffer de snapshots. Si es null/empty, se resuelve por
    /// env var <c>AETHRA_SATELLITE_BUFFER_PATH</c> o default por OS (Linux: <c>/var/lib/aethra/buffer.db</c>,
    /// Windows: <c>%LOCALAPPDATA%\aethra\buffer.db</c>).
    /// </summary>
    public string? BufferPath { get; set; }
}
