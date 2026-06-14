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
    /// Tras cada build git-mode, poda el build cache del runtime no usado en las últimas N horas
    /// (BuildKit/buildah acumula capas intermedias sin límite — ~15 GB por ciclo de builds → fuga de
    /// disco). Conserva el cache reciente para que los rebuilds sigan siendo rápidos. 0 o negativo
    /// desactiva el prune. Default 48 (2 días).
    /// </summary>
    public int BuildCacheMaxAgeHours { get; set; } = 48;

    /// <summary>
    /// Path al archivo SQLite del buffer de snapshots. Si es null/empty, se resuelve por
    /// env var <c>AETHRA_SATELLITE_BUFFER_PATH</c> o default por OS (Linux: <c>/var/lib/aethra/buffer.db</c>,
    /// Windows: <c>%LOCALAPPDATA%\aethra\buffer.db</c>).
    /// </summary>
    public string? BufferPath { get; set; }
}
