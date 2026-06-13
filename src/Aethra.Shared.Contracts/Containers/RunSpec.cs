namespace Aethra.Shared.Contracts.Containers;

/// <summary>
/// Especificación agnóstica de runtime para arrancar un contenedor.
/// </summary>
/// <param name="ContainerName">Nombre humano del contenedor (único por host).</param>
/// <param name="ImageRef">Referencia de la imagen a ejecutar (debe existir local o pullable).</param>
/// <param name="Env">Variables de entorno a inyectar.</param>
/// <param name="Ports">Bindings de puertos contenedor → host.</param>
/// <param name="Volumes">Bindings de volúmenes o paths host → contenedor.</param>
/// <param name="Command">Comando override (equivalente a <c>CMD</c>). Si es null, usa el de la imagen.</param>
/// <param name="Healthcheck">Definición de healthcheck override. Si es null, usa el de la imagen.</param>
/// <param name="NetworkName">Red Docker/Podman a la que se une el contenedor (null = default bridge).</param>
/// <param name="RestartPolicy">Política de reinicio (<c>no</c>, <c>always</c>, <c>on-failure</c>, <c>unless-stopped</c>).</param>
public sealed record RunSpec(
    string ContainerName,
    string ImageRef,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<PortBinding> Ports,
    IReadOnlyList<VolumeBinding> Volumes,
    IReadOnlyList<string>? Command,
    HealthcheckSpec? Healthcheck,
    string? NetworkName,
    string? RestartPolicy);

/// <summary>Mapeo de un puerto del contenedor a un puerto del host.</summary>
/// <param name="ContainerPort">Puerto interno expuesto por el contenedor.</param>
/// <param name="HostPort">Puerto del host. Si es null, el runtime asigna uno ephemeral.</param>
/// <param name="Protocol">Protocolo: <c>tcp</c> o <c>udp</c> (minúsculas).</param>
/// <param name="HostIp">
/// Interfaz del host a la que se ata el puerto. Si es null, el runtime usa <c>127.0.0.1</c>
/// (loopback) — los contenedores nativos se alcanzan por DNS de Docker dentro de la red del
/// proxy, así que el puerto en el host es solo para health-check/diagnóstico y NO debe quedar
/// público. Para exponer públicamente (p. ej. un ingress SMTP) pasar <c>"0.0.0.0"</c> explícito.
/// </param>
public sealed record PortBinding(int ContainerPort, int? HostPort, string Protocol, string? HostIp = null);

/// <summary>Mapeo de un volumen o path del host hacia el contenedor.</summary>
/// <param name="NameOrHostPath">Nombre del named volume o ruta absoluta del host (bind mount).</param>
/// <param name="ContainerPath">Ruta absoluta dentro del contenedor.</param>
/// <param name="ReadOnly">Si el binding es de solo lectura (<c>:ro</c>).</param>
public sealed record VolumeBinding(string NameOrHostPath, string ContainerPath, bool ReadOnly);

/// <summary>Definición de healthcheck override para un contenedor.</summary>
/// <param name="Test">Comando del healthcheck. Primer elemento suele ser <c>CMD</c> o <c>CMD-SHELL</c>.</param>
/// <param name="IntervalSeconds">Intervalo entre ejecuciones del healthcheck.</param>
/// <param name="Retries">Número de fallos consecutivos antes de marcar el contenedor unhealthy.</param>
/// <param name="TimeoutSeconds">Tiempo máximo de cada ejecución antes de considerarla fallida.</param>
/// <param name="StartPeriodSeconds">Periodo inicial durante el cual los fallos no cuentan (warm-up).</param>
public sealed record HealthcheckSpec(
    IReadOnlyList<string> Test,
    int IntervalSeconds,
    int Retries,
    int? TimeoutSeconds,
    int? StartPeriodSeconds);
