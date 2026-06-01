using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Shared.Contracts.Containers;
using Aethra.Shared.Contracts.Deployments;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Contracts.Settings;
using Aethra.Shared.Infrastructure.Outbox;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Build;

/// <summary>
/// Implementación del orquestador de builds. F9.3 entrega un pipeline en MODO DRY-RUN:
///
/// - La state machine avanza completa (Queued → Cloning → Building → Pushing → Completed),
///   se persisten transiciones y se emiten logs reales.
/// - El paso por <see cref="ISatelliteRpcClient.SendBuildAsync"/> está stubeado
///   (<see cref="NotImplementedException"/>). Lo intentamos para validar el call site y, si
///   lanza, registramos un warning ("satellite RPC pendiente F9.3.5") y seguimos.
/// - El <c>ImageRef</c> que persistimos es un placeholder <c>dry-run-image:&lt;sha&gt;</c>.
///   F9.3.5 cableará BuildKit/Podman + push real y reemplazará este placeholder por la
///   referencia real del registry interno.
///
/// La razón de mantener el estado de "Completed" aunque la imagen no exista físicamente
/// es que el resto del flujo (Deployment, UI, integration events) ya puede ejercitarse en
/// dry-run sin esperar al satélite.
/// </summary>
public sealed class BuildOrchestrator(
    DeploymentsDbContext db,
    ITemplateLookup templateLookup,
    ISatelliteRpcClient satelliteClient,
    ISatelliteConnectionRegistry satelliteRegistry,
    IOutboxWriter<DeploymentsDbContext> outbox,
    IIntegrationCredentialResolver credentialResolver,
    IClock clock,
    ILogger<BuildOrchestrator> logger) : IBuildOrchestrator
{
    // F9.3.5: cuando exista una credencial para el registry interno, el orquestador la
    // descifrará vía credentialResolver y la inyectará en el satellite RPC. Por ahora solo
    // registramos un log informativo sobre la existencia del credential.
    private const string InternalRegistryCredentialName = "registry:internal";

    public async Task RunAsync(BuildId buildId, CancellationToken ct)
    {
        var build = await db.Builds
            .Include(b => b.Logs)
            .FirstOrDefaultAsync(b => b.Id == buildId, ct)
            .ConfigureAwait(false);

        if (build is null)
        {
            logger.LogWarning("BuildOrchestrator: build {Id} no existe — skip", buildId);
            return;
        }

        if (build.Status.IsTerminal())
        {
            logger.LogInformation(
                "BuildOrchestrator: build {Id} ya está en estado terminal {Status} — skip",
                buildId, build.Status);
            return;
        }

        var template = await templateLookup.GetByIdAsync(build.TemplateId, ct).ConfigureAwait(false);
        if (template is null)
        {
            FailAndPersist(build, "template_not_found",
                $"Template '{build.TemplateId}' no existe (¿borrado tras encolar?).");
            await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

            // === Clone ===
            build.Transition(BuildStatus.Cloning, clock.UtcNow);
            build.AppendLog(BuildLogLevel.Info, "cloning",
                $"dry-run: clone simulado {template.GitRepoUrl}@{build.GitRef} (sha={build.GitSha})",
                clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Build ===
            build.Transition(BuildStatus.Building, clock.UtcNow);
            build.AppendLog(BuildLogLevel.Info, "building",
                $"dry-run: build simulado con Dockerfile={template.DockerfilePath}, "
                + $"base_dir={template.BaseDirectory}, build_type={template.BuildType}",
                clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Intento real al satélite — F9.3 lanza NotImplementedException. Atrapamos para
            // dejar evidencia en el log del build de que el cableado no está listo.
            var credentialExists = await credentialResolver
                .ExistsAsync(InternalRegistryCredentialName, ct).ConfigureAwait(false);
            if (!credentialExists)
            {
                build.AppendLog(BuildLogLevel.Warn, "building",
                    $"Credencial '{InternalRegistryCredentialName}' no configurada — "
                    + "F9.3.5 la requerirá para push.",
                    clock.UtcNow);
            }

            var shortSha = build.GitSha.Length >= 7 ? build.GitSha[..7] : build.GitSha;
            var placeholderImageRef = $"aethra-image:{template.Slug}-{shortSha}";

            // F9.8C: el build necesita un satélite connecté para ejecutar BuildImage real.
            // F9.4/F9.5 cablearán routing por VM/Cluster; por ahora elegimos cualquier satélite
            // conectado (single-VM smoke). Si ninguno está conectado, falla con errorCode estable.
            var targetVmId = satelliteRegistry.ConnectedVmIds.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(targetVmId))
            {
                FailAndPersist(build, "no_satellite",
                    "No hay satélite conectado al central. Verificar que el satélite esté corriendo "
                    + "y que su token sea válido.");
                await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
                return;
            }

            build.AppendLog(BuildLogLevel.Info, "building",
                $"Despachando BuildImage al satélite vmId={targetVmId}",
                clock.UtcNow);

            BuildResult buildResult;
            try
            {
                var spec = new BuildSpec(
                    ImageRef: placeholderImageRef,
                    BuildContextTarGz: Array.Empty<byte>(),
                    DockerfilePath: template.DockerfilePath,
                    BuildArgs: new Dictionary<string, string>(),
                    BuildSecrets: null);
                buildResult = await satelliteClient
                    .SendBuildAsync(vmId: targetVmId, spec: spec, pushTo: null, ct: ct)
                    .ConfigureAwait(false);
            }
            catch (SatelliteNotConnectedException ex)
            {
                FailAndPersist(build, "no_satellite", ex.Message);
                await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException ex)
            {
                FailAndPersist(build, "satellite_timeout", ex.Message);
                await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
                return;
            }

            // Persistir los logs que devolvió el runtime (Docker/Podman) si los hay.
            foreach (var line in buildResult.LogLines)
            {
                build.AppendLog(BuildLogLevel.Info, "building", line, clock.UtcNow);
            }

            if (!buildResult.Success)
            {
                FailAndPersist(build, "runtime_failed",
                    buildResult.ErrorMessage ?? "Build falló en el satélite sin mensaje.");
                await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
                return;
            }

            // === Push ===
            build.Transition(BuildStatus.Pushing, clock.UtcNow);
            build.AppendLog(BuildLogLevel.Info, "pushing",
                $"Imagen construida: {placeholderImageRef} (imageId={buildResult.ImageId})",
                clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Complete ===
            totalStopwatch.Stop();
            build.RecordImageRef(placeholderImageRef, totalStopwatch.ElapsedMilliseconds, clock.UtcNow);
            build.Complete(clock.UtcNow);

            await outbox.EnqueueAsync(new BuildCompletedIntegrationEvent(
                BuildId: build.Id.ToString(),
                TemplateId: build.TemplateId,
                ImageRef: placeholderImageRef,
                GitSha: build.GitSha,
                CompletedAt: clock.UtcNow), ct).ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            logger.LogInformation(
                "Build {Id} (template={Template}, sha={Sha}) completado en {Ms} ms",
                build.Id, build.TemplateId, shortSha, totalStopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // El worker está cerrando — dejamos el build en estado intermedio. El recovery
            // de F9.3.5 lo retomará.
            logger.LogInformation("BuildOrchestrator: cancelado durante build {Id}", build.Id);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BuildOrchestrator: falla inesperada en build {Id}", build.Id);
            FailAndPersist(build, "internal_error", ex.Message);
            await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
        }
    }

    private void FailAndPersist(Domain.Build.Build build, string code, string message)
    {
        // Si el build ya falló antes (p.ej. dentro del try), no duplicamos transición.
        if (!build.Status.IsTerminal())
        {
            build.Fail(code, message, clock.UtcNow);
        }
    }

    private async Task PersistAndPublishFailureAsync(Domain.Build.Build build, CancellationToken ct)
    {
        await outbox.EnqueueAsync(new BuildFailedIntegrationEvent(
            BuildId: build.Id.ToString(),
            TemplateId: build.TemplateId,
            ErrorCode: build.ErrorCode ?? "unknown",
            ErrorMessage: build.ErrorMessage ?? string.Empty,
            FailedAt: clock.UtcNow), ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
