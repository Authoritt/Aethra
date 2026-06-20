namespace Aethra.Satellite;

public sealed class SatelliteOptions
{
    public string CentralUrl { get; set; } = "http://localhost:5080";
    public string Token { get; set; } = string.Empty;
    public int MetricsIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Cada cuántos segundos el satélite reporta el inventario de contenedores del host (TODOS,
    /// gestionados por Aethra o no) con stats de uso (CPU/mem/red/disco). Más pesado que las métricas
    /// de host (una llamada de stats por contenedor corriendo) → cadencia más lenta. Es estado actual,
    /// no time-series: si el central está caído se omite (no se bufferea). 0 o negativo lo desactiva.
    /// Default 15.
    /// </summary>
    public int ContainerReportIntervalSeconds { get; set; } = 15;

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
    /// desactiva el prune. Sólo se usa si <see cref="BuildCacheKeepStorageGb"/> &lt;= 0 (el tope por
    /// tamaño es preferible). Default 48 (2 días).
    /// </summary>
    public int BuildCacheMaxAgeHours { get; set; } = 48;

    /// <summary>
    /// Tope DURO de tamaño del build cache en GB (<c>docker builder prune --reserved-space</c>). Cuando
    /// &gt; 0 acota el cache por tamaño en vez de por edad: deja a lo sumo estos GB del cache más
    /// reciente y borra el resto tras cada build Y en el janitor periódico. Robusto frente a ráfagas de
    /// builds del mismo día (que el filtro por edad NO reclama → fue la causa de que el disco se
    /// volviera a llenar). 0 o negativo desactiva el tope por tamaño. Default 5.
    /// </summary>
    public int BuildCacheKeepStorageGb { get; set; } = 5;

    /// <summary>
    /// Cada cuántas horas corre el janitor de disco (backstop periódico, independiente de los builds):
    /// poda build cache al tope de tamaño + imágenes colgantes. Cubre el caso de que los builds paren
    /// (los huérfanos no se reclamarían) y el de builds que NO pasan por el satélite (ej. rebuild manual
    /// del central). 0 o negativo lo desactiva. Default 6.
    /// </summary>
    public int DiskJanitorIntervalHours { get; set; } = 6;

    /// <summary>
    /// Directorio base donde el central guarda blobs (p.ej. backups) en este satélite vía RPC
    /// (file store). Si es null/empty se resuelve a <c>{DataVolumePath}/aethra-store</c> o
    /// <c>/var/lib/aethra/store</c>. Permite usar el disco libre de satélites como almacenamiento.
    /// Env: <c>AETHRA_REMOTE_STORE_PATH</c>.
    /// </summary>
    public string? RemoteStorePath { get; set; }

    /// <summary>
    /// Path al archivo SQLite del buffer de snapshots. Si es null/empty, se resuelve por
    /// env var <c>AETHRA_SATELLITE_BUFFER_PATH</c> o default por OS (Linux: <c>/var/lib/aethra/buffer.db</c>,
    /// Windows: <c>%LOCALAPPDATA%\aethra\buffer.db</c>).
    /// </summary>
    public string? BufferPath { get; set; }
}
