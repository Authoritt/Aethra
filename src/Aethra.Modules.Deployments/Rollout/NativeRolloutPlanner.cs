using Aethra.Shared.Contracts.Containers;

namespace Aethra.Modules.Deployments.Rollout;

/// <summary>
/// Un servicio cuyo contenedor fue (o está a punto de ser) sustituido en un rollout nativo, junto con
/// la identidad de la revisión que había ANTES — lo único que permite volver atrás.
/// </summary>
/// <param name="ServiceName">Nombre del servicio en el template (clave de su <c>RunSpec</c>).</param>
/// <param name="ContainerName">Nombre estable del contenedor: <c>{slug}-{servicio}</c>.</param>
/// <param name="NewImageRef">Imagen con la que se está reemplazando.</param>
/// <param name="PreviousImageRef">
/// Imagen que corría antes, leída de <see cref="ContainerInfo.Image"/>. <c>null</c> = no había
/// contenedor previo (primer deploy del servicio) y por tanto no hay revisión a la que volver.
/// </param>
/// <param name="PreviousContainerId">
/// Id del contenedor previo, para <c>Deployment.RecordOldContainer</c> (el agregado exige tenerlo
/// registrado antes de admitir la transición a <c>RolledBack</c>).
/// </param>
/// <param name="OldContainerRemoved">
/// Si el <c>SendRemoveAsync</c> del contenedor previo se ejecutó SIN error. Es la diferencia entre
/// "lo sustituí" y "lo intenté": cuando el remove falla, el contenedor viejo sigue vivo y sano, y
/// un rollback que lo borre para recrearlo destruiría producción sin necesidad (G2 del PR #101).
/// </param>
public sealed record ServiceReplacement(
    string ServiceName,
    string ContainerName,
    string NewImageRef,
    string? PreviousImageRef,
    string? PreviousContainerId,
    bool OldContainerRemoved = false);

/// <summary>Qué hacer con un servicio al deshacer un rollout fallido.</summary>
public enum RollbackAction
{
    /// <summary>Retirar el contenedor nuevo y volver a levantar la imagen previa con la misma spec.</summary>
    RestorePrevious = 0,

    /// <summary>
    /// No había revisión previa (primer deploy del servicio): no se restaura nada y el contenedor
    /// nuevo se DEJA en su sitio a propósito, porque sus logs son el único diagnóstico del fallo y
    /// no hay tráfico anterior que proteger.
    /// </summary>
    LeaveForDiagnosis = 1,

    /// <summary>
    /// El contenedor previo NUNCA llegó a borrarse (su <c>remove</c> falló), así que la revisión
    /// anterior sigue viva y sirviendo. No se toca: retirarla para recrearla convertiría un rollout
    /// fallido e inocuo en la destrucción de un contenedor sano, y si el <c>run</c> de restauración
    /// fallara, el servicio quedaría caído por culpa del propio rollback.
    /// </summary>
    LeaveIntact = 2,

    /// <summary>
    /// Había revisión previa pero NO se puede restaurar con garantías, porque su referencia de
    /// imagen es la MISMA que la del despliegue nuevo (típico cuando un rebuild de git reutiliza el
    /// tag <c>{slug}-{svc}:{shortSha}</c>). Ese tag ya apunta a la imagen recién construida, así que
    /// "restaurar" relanzaría exactamente la revisión que acaba de fallar. Se reporta como fallo del
    /// rollback en vez de simularlo: un rollback que no puede cumplir es peor mentira que uno que
    /// avisa. Cerrarlo de verdad exige que el satélite devuelva el ID/digest de la imagen.
    /// </summary>
    CannotRestore = 3,
}

/// <summary>Un paso concreto del plan de deshacer, ya resuelto (sin decisiones pendientes).</summary>
public sealed record RollbackStep(
    string ServiceName,
    string ContainerName,
    RollbackAction Action,
    string? RestoreImageRef);

/// <summary>
/// Foto del runtime tomada antes de la fase destructiva, CON su fiabilidad explícita.
///
/// <para>
/// El campo <see cref="Succeeded"/> no es decorativo: una lista vacía es AMBIGUA. Puede significar
/// "esta Instance no tiene contenedores todavía" (primer deploy, seguro destruir nada) o "no pude
/// preguntarle al satélite" (un timeout transitorio). Confundirlas es lo que convierte un deploy con
/// rollback en uno destructivo: si el listado falla y aun así seguimos, cada servicio se clasifica
/// como "sin revisión previa", se borra su contenedor con <c>force:true</c> y no queda nada que
/// restaurar. Por eso la fiabilidad viaja con los datos y no se infiere del <c>Count</c>.
/// </para>
/// </summary>
public sealed record ContainerSnapshot(bool Succeeded, IReadOnlyList<ContainerInfo> Containers)
{
    /// <summary>Foto tomada con éxito. Una lista vacía aquí SÍ significa "no hay contenedores".</summary>
    public static ContainerSnapshot Taken(IReadOnlyList<ContainerInfo> containers) => new(true, containers);

    /// <summary>No se pudo consultar el runtime: no sabemos qué hay, así que no se destruye nada.</summary>
    public static ContainerSnapshot Unavailable() => new(false, []);
}

/// <summary>
/// Veredicto sobre si es seguro sustituir el contenedor de un servicio.
/// </summary>
/// <param name="CanProceed">Si se puede ejecutar la fase destructiva (remove + run).</param>
/// <param name="Replacement">Reemplazo capturado; no nulo si y solo si <paramref name="CanProceed"/>.</param>
/// <param name="AbortReason">Motivo del rechazo; no nulo si y solo si NO se puede proceder.</param>
public sealed record ReplacementDecision(
    bool CanProceed,
    ServiceReplacement? Replacement,
    string? AbortReason);

/// <summary>
/// OT-006 <c>#49</c>/<c>#50</c> — decide QUÉ hay que deshacer cuando un rollout nativo multi-servicio
/// falla, y en qué orden. Función pura: el runner solo ejecuta el plan.
///
/// <para><b>Por qué el diseño es "capturar y restaurar" y no "mantener el viejo vivo".</b>
/// La pregunta obvia es por qué no se levanta el reemplazo ANTES de borrar el anterior. Cuatro
/// colisiones lo impiden, todas verificadas en el código de este repo:
/// <list type="number">
/// <item><b>Nombre de contenedor</b>: <c>DockerContainerRuntime.RunContainerAsync</c> crea con
/// <c>CreateContainerParameters.Name = spec.ContainerName</c> y no maneja el conflicto; Docker
/// rechaza un nombre ya tomado aunque el contenedor viejo esté PARADO. Y
/// <c>ISatelliteRpcClient</c> no expone <c>rename</c>, así que no hay forma de apartar al viejo.</item>
/// <item><b>Puerto publicado</b>: el runner publica <c>svc.HostPort</c> en <c>0.0.0.0</c> cuando está
/// definido. En producción <c>relaycore-worker</c> publica <c>0.0.0.0:25-&gt;25/tcp</c>: con el viejo
/// vivo, el nuevo fallaría con "port is already allocated".</item>
/// <item><b>Alias DNS de la red</b>: el backend de las rutas es <c>http://{slug}-{svc}:{port}</c>,
/// resuelto por el DNS embebido de Docker en <c>aethra-net</c> a partir del NOMBRE del contenedor.
/// Dos contenedores no pueden responder al mismo nombre.</item>
/// <item><b>Propiedad de rutas</b>: <c>RouteOwnershipRules</c> reconoce como propias las rutas cuyo
/// backend empieza por <c>http://{slug}-{svc}:</c>. Un contenedor con nombre temporal produciría un
/// backend que NO casa ese prefijo, reeditando la clase de bug de OT-001.</item>
/// </list>
/// Y por encima de todo eso hay un bloqueo de producto: mantener dos contenedores VIVOS de la misma
/// app es el split-brain que <c>NativeDeployRunner.CleanupStaleContainersAsync</c> documenta como
/// observado en producción (ambos corren sus hosted services contra el mismo Postgres, reclaman
/// trabajo y lo expiran). Por eso el invariante que sí cabe aquí es más modesto y más seguro:
/// <b>el rollout puede fallar, pero nunca deja al servicio sin la revisión que tenía</b>. La ventana
/// de indisponibilidad por servicio sigue existiendo (igual que antes); lo que desaparece es que sea
/// destructiva. El zero-downtime real exige un RPC de rename o puertos efímeros forzados: decisión de
/// producto encolada, no improvisada aquí.
/// </para>
/// </summary>
public static class NativeRolloutPlanner
{
    /// <summary>
    /// Fotografía la revisión previa de un servicio a partir del listado de contenedores tomado
    /// ANTES de tocar nada. Debe llamarse antes del <c>SendRemoveAsync</c>: después, la información
    /// ya no existe en ninguna parte (el contenedor se borra con <c>force:true</c>).
    /// </summary>
    public static ServiceReplacement Capture(
        string serviceName,
        string containerName,
        string newImageRef,
        IReadOnlyList<ContainerInfo> containersBefore)
    {
        ContainerInfo? previous = null;
        for (var i = 0; i < containersBefore.Count; i++)
        {
            if (string.Equals(containersBefore[i].Name, containerName, StringComparison.Ordinal))
            {
                previous = containersBefore[i];
                break;
            }
        }

        // Un contenedor previo sin imagen legible no es restaurable: se trata como "no había".
        var previousImage = string.IsNullOrWhiteSpace(previous?.Image) ? null : previous!.Image;
        var previousId = string.IsNullOrWhiteSpace(previous?.Id) ? null : previous!.Id;

        return new ServiceReplacement(serviceName, containerName, newImageRef, previousImage, previousId);
    }

    /// <summary>
    /// Decide si se puede sustituir el contenedor de un servicio, y con qué información de vuelta.
    ///
    /// <para><b>Falla cerrado.</b> Si la foto del runtime no se pudo tomar
    /// (<see cref="ContainerSnapshot.Succeeded"/> = <c>false</c>) se RECHAZA la sustitución, aunque
    /// el satélite pudiera atender el <c>run</c>. El razonamiento: el <c>remove</c> es
    /// <c>force:true</c> e irreversible, y sin la foto no sabemos si hay una revisión viva que
    /// perderíamos ni con qué imagen restaurarla. Un deploy que no ocurre se reintenta; un
    /// contenedor de producción borrado sin copia de su identidad, no. Abortar es estrictamente
    /// más barato que el fallo que evita.</para>
    ///
    /// <para>Una foto EXITOSA en la que el servicio no aparece sí es información: es un primer
    /// deploy legítimo y se procede sin revisión previa que restaurar.</para>
    /// </summary>
    public static ReplacementDecision DecideReplacement(
        string serviceName,
        string containerName,
        string newImageRef,
        ContainerSnapshot snapshot)
    {
        if (!snapshot.Succeeded)
        {
            return new ReplacementDecision(false, null,
                $"no se pudo consultar el estado del runtime antes de sustituir '{serviceName}': "
                + $"sin esa foto, borrar {containerName} seria destructivo e irreversible "
                + "(no sabriamos si hay revision previa ni con que imagen restaurarla).");
        }
        return new ReplacementDecision(
            true, Capture(serviceName, containerName, newImageRef, snapshot.Containers), null);
    }

    /// <summary>
    /// Plan de deshacer para los reemplazos YA aplicados, en orden inverso al de aplicación (LIFO):
    /// el último cambio ejecutado es el primero en revertirse, de modo que el sistema recorre los
    /// mismos estados intermedios en sentido contrario. Determinista y sin I/O.
    ///
    /// <para>
    /// La lista de entrada incluye al servicio que falló: se anota como reemplazo ANTES de su
    /// <c>remove</c>, así que si el <c>run</c> posterior falla, ese servicio también quedó sin
    /// contenedor y también entra en el plan. Ese es el bug <c>#50</c>: el <c>foreach</c> retornaba
    /// <c>Fail</c> dejando los servicios <c>1..k</c> ya sustituidos y sin recuperación.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RollbackStep> PlanRollback(IReadOnlyList<ServiceReplacement> replacements)
    {
        var steps = new List<RollbackStep>(replacements.Count);
        for (var i = replacements.Count - 1; i >= 0; i--)
        {
            var r = replacements[i];
            steps.Add(new RollbackStep(r.ServiceName, r.ContainerName, DecideAction(r), r.PreviousImageRef));
        }
        return steps;
    }

    /// <summary>
    /// Qué hacer con un servicio concreto al deshacer. El orden de las guardas importa: cada una
    /// descarta un motivo distinto por el que restaurar sería inútil o dañino, y solo lo que
    /// sobrevive a las tres se restaura de verdad.
    /// </summary>
    private static RollbackAction DecideAction(ServiceReplacement r)
    {
        // 1) No había nada antes: primer deploy. El contenedor nuevo se queda para diagnosticar.
        if (string.IsNullOrWhiteSpace(r.PreviousImageRef))
        {
            return RollbackAction.LeaveForDiagnosis;
        }

        // 2) El remove falló: la revisión anterior nunca se fue y sigue sirviendo. Tocarla es el
        //    único modo de convertir este fallo en una caída.
        if (!r.OldContainerRemoved)
        {
            return RollbackAction.LeaveIntact;
        }

        // 3) El tag previo y el nuevo son el mismo: restaurarlo relanzaría la imagen que ha fallado.
        //    Mejor decir que no se puede que fingir que se hizo.
        if (string.Equals(r.PreviousImageRef, r.NewImageRef, StringComparison.Ordinal))
        {
            return RollbackAction.CannotRestore;
        }

        return RollbackAction.RestorePrevious;
    }
}
