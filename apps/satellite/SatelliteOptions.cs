namespace Aethra.Satellite;

public sealed class SatelliteOptions
{
    public string CentralUrl { get; set; } = "http://localhost:5080";
    public string Token { get; set; } = string.Empty;
    public int MetricsIntervalSeconds { get; set; } = 5;
    public string ContainerRuntime { get; set; } = "docker";
    public string? DataVolumePath { get; set; }

    /// <summary>
    /// Retención de imágenes tras cada build git-mode: cuántos tags más recientes conservar por
    /// repositorio (ej. 'aethra/myapp-api'). Los más viejos se borran (sin --force, así que
    /// nunca toca imágenes en uso) para que los flujos de build/deploy no saturen el disco.
    /// 0 o negativo desactiva la retención. Default 3 (imagen corriendo + margen de rollback).
    /// </summary>
    public int ImageRetentionKeep { get; set; } = 3;

    /// <summary>
    /// Path al archivo SQLite del buffer de snapshots. Si es null/empty, se resuelve por
    /// env var <c>AETHRA_SATELLITE_BUFFER_PATH</c> o default por OS (Linux: <c>/var/lib/aethra/buffer.db</c>,
    /// Windows: <c>%LOCALAPPDATA%\aethra\buffer.db</c>).
    /// </summary>
    public string? BufferPath { get; set; }
}
