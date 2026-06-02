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
/// Implementación del orquestador de builds. F10.1 entrega el pipeline REAL:
///
/// - La state machine avanza completa (Queued → Cloning → Building → Pushing → Completed),
///   se persisten transiciones y se emiten logs reales.
/// - <b>Clone</b>: <see cref="IBuildContextBuilder"/> clona el repo, materializa el commit y
///   empaqueta el contexto en un tar.gz real (gap 1 cerrado).
/// - <b>Build</b>: el contexto + el <c>ImageRef</c> (<c>aethra/&lt;slug&gt;:&lt;shortSha&gt;</c>) se
///   despachan al satélite vía <see cref="ISatelliteRpcClient.SendBuildAsync"/>, que ejecuta
///   <c>docker build</c> (Docker.DotNet) y tagea la imagen. Si no hay satélite conectado, el
///   build falla con errorCode estable <c>no_satellite</c> (no se finge éxito).
/// - <b>Push</b>: en single-VM la imagen queda construida/tageada localmente en el mismo satélite
///   donde luego corre el deploy, así que no se hace push a un registry (<c>pushTo: null</c>).
/// </summary>
public sealed class BuildOrchestrator(
    DeploymentsDbContext db,
    ITemplateLookup templateLookup,
    IBuildContextBuilder buildContextBuilder,
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

        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // === Clone ===
            build.Transition(BuildStatus.Cloning, clock.UtcNow);
            build.AppendLog(BuildLogLevel.Info, "cloning",
                $"Clonando {template.GitRepoUrl}@{build.GitRef} (sha solicitado={build.GitSha})",
                clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // F10.1: clone real + empaquetado del contexto. El branch a clonar sale del ref que
            // disparó el build (o el default del template); el commit exacto viaja en build.GitSha
            // y se materializa con checkout. Si falla, el build termina con errorCode estable.
            BuildContextResult context;
            try
            {
                var cloneBranch = string.IsNullOrWhiteSpace(build.GitRef)
                    ? template.Branch
                    : NormalizeRef(build.GitRef);
                context = await buildContextBuilder
                    .BuildAsync(template.GitRepoUrl, cloneBranch, build.GitSha, template.BaseDirectory, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                FailAndPersist(build, "clone_failed",
                    $"No se pudo clonar/empaquetar el repositorio: {ex.Message}",
                    totalStopwatch.ElapsedMilliseconds);
                await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
                return;
            }

            foreach (var line in context.Log)
            {
                build.AppendLog(BuildLogLevel.Info, "cloning", line, clock.UtcNow);
            }

            // El SHA materializado (resolvedSha) puede diferir del solicitado si el checkout cayó
            // al HEAD del branch; la imagen se tagea con el SHA real para trazabilidad.
            var resolvedSha = string.IsNullOrWhiteSpace(context.ResolvedSha)
                ? build.GitSha
                : context.ResolvedSha;
            var shortSha = resolvedSha.Length >= 7 ? resolvedSha[..7] : resolvedSha;
            var imageRef = $"aethra/{template.Slug}:{shortSha}".ToLowerInvariant();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Build ===
            build.Transition(BuildStatus.Building, clock.UtcNow);
            // F11.2: el mensaje principal depende del modo — para Nixpacks no aplica Dockerfile.
            var buildMode = MapBuildMode(template.BuildType);
            if (buildMode == BuildMode.Nixpacks)
            {
                build.AppendLog(BuildLogLevel.Info, "building",
                    $"Build via Nixpacks (auto-detect de lenguaje), base_dir={template.BaseDirectory}, image={imageRef}",
                    clock.UtcNow);
            }
            else if (buildMode == BuildMode.DockerCompose)
            {
                build.AppendLog(BuildLogLevel.Info, "building",
                    $"Build via DockerCompose={template.ComposeFilePath ?? "docker-compose.yml"}, "
                    + $"base_dir={template.BaseDirectory}, image={imageRef}",
                    clock.UtcNow);
            }
            else
            {
                build.AppendLog(BuildLogLevel.Info, "building",
                    $"Build con Dockerfile={template.DockerfilePath}, base_dir={template.BaseDirectory}, "
                    + $"build_type={template.BuildType}, image={imageRef}",
                    clock.UtcNow);
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Credencial del registry interno: si en el futuro se hace push a un registry, el
            // orquestador la descifrará vía credentialResolver. En build local single-VM no se usa.
            var credentialExists = await credentialResolver
                .ExistsAsync(InternalRegistryCredentialName, ct).ConfigureAwait(false);
            if (!credentialExists)
            {
                build.AppendLog(BuildLogLevel.Info, "building",
                    $"Credencial '{InternalRegistryCredentialName}' no configurada — "
                    + "build local sin push (single-VM).",
                    clock.UtcNow);
            }

            // El build necesita un satélite conectado para ejecutar BuildImage. Si ninguno está
            // conectado, falla con errorCode estable.
            var targetVmId = satelliteRegistry.ConnectedVmIds.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(targetVmId))
            {
                FailAndPersist(build, "no_satellite",
                    "No hay satélite conectado al central. Verificar que el satélite esté corriendo "
                    + "y que su token sea válido.",
                    totalStopwatch.ElapsedMilliseconds);
                await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
                return;
            }

            build.AppendLog(BuildLogLevel.Info, "building",
                $"Despachando BuildImage al satélite vmId={targetVmId} "
                + $"({context.TarGz.Length / 1024} KiB de contexto)",
                clock.UtcNow);

            BuildResult buildResult;
            try
            {
                // F11.2: el satélite necesita el modo + paths específicos por modo.
                // Para Nixpacks, no se usa Dockerfile (se ignora); para DockerCompose se manda
                // ComposeFilePath. El default sigue siendo Dockerfile para preservar compat.
                var spec = new BuildSpec(
                    ImageRef: imageRef,
                    BuildContextTarGz: context.TarGz,
                    DockerfilePath: template.DockerfilePath,
                    BuildArgs: new Dictionary<string, string>(),
                    BuildSecrets: null,
                    Mode: buildMode,
                    ComposeFilePath: buildMode == BuildMode.DockerCompose ? template.ComposeFilePath : null,
                    NixpacksConfig: null);
                buildResult = await satelliteClient
                    .SendBuildAsync(vmId: targetVmId, spec: spec, pushTo: null, ct: ct)
                    .ConfigureAwait(false);
            }
            catch (SatelliteNotConnectedException ex)
            {
                FailAndPersist(build, "no_satellite", ex.Message, totalStopwatch.ElapsedMilliseconds);
                await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException ex)
            {
                // F9.10 D2: capturar ANTES de Fail() para que BuildDurationMs refleje el tiempo
                // real del RPC (típicamente varios minutos), no ~1ms del clock posterior.
                FailAndPersist(build, "satellite_timeout", ex.Message, totalStopwatch.ElapsedMilliseconds);
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
                    buildResult.ErrorMessage ?? "Build falló en el satélite sin mensaje.",
                    totalStopwatch.ElapsedMilliseconds);
                await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
                return;
            }

            // === Push ===
            build.Transition(BuildStatus.Pushing, clock.UtcNow);
            build.AppendLog(BuildLogLevel.Info, "pushing",
                $"Imagen construida: {imageRef} (imageId={buildResult.ImageId})",
                clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // === Complete ===
            totalStopwatch.Stop();
            build.RecordImageRef(imageRef, totalStopwatch.ElapsedMilliseconds, clock.UtcNow);
            build.Complete(clock.UtcNow);

            await outbox.EnqueueAsync(new BuildCompletedIntegrationEvent(
                BuildId: build.Id.ToString(),
                TemplateId: build.TemplateId,
                ImageRef: imageRef,
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
            FailAndPersist(build, "internal_error", ex.Message, totalStopwatch.ElapsedMilliseconds);
            await PersistAndPublishFailureAsync(build, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Normaliza un git ref a su shorthand de branch: <c>refs/heads/v2</c> → <c>v2</c>. LibGit2Sharp
    /// (CloneOptions.BranchName) espera el shorthand, no el ref completo del webhook.
    /// </summary>
    private static string NormalizeRef(string gitRef)
    {
        const string prefix = "refs/heads/";
        return gitRef.StartsWith(prefix, StringComparison.Ordinal) ? gitRef[prefix.Length..] : gitRef;
    }

    /// <summary>
    /// Mapea el string del <c>template.BuildType</c> (que viene del read-model) al enum
    /// <see cref="BuildMode"/> del contrato satélite. Default: Dockerfile (compat).
    /// </summary>
    private static BuildMode MapBuildMode(string buildType)
        => buildType?.Trim().ToLowerInvariant() switch
        {
            "nixpacks" => BuildMode.Nixpacks,
            "dockercompose" => BuildMode.DockerCompose,
            "docker-compose" => BuildMode.DockerCompose,
            _ => BuildMode.Dockerfile,
        };

    private void FailAndPersist(Domain.Build.Build build, string code, string message,
        long? durationMs = null)
    {
        // Si el build ya falló antes (p.ej. dentro del try), no duplicamos transición.
        if (!build.Status.IsTerminal())
        {
            // F9.10 D2: durationMs viene del Stopwatch del orquestador para fallos tardíos
            // (timeout, runtime_failed, internal_error). Para fallos tempranos pre-stopwatch
            // (template_not_found) viaja como null y BuildDurationMs queda en null — correcto.
            build.Fail(code, message, clock.UtcNow, durationMs);
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
