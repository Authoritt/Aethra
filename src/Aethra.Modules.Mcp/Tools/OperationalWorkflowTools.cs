using System.ComponentModel;
using Aethra.Modules.Deployments.Domain.Build;
using Aethra.Modules.Deployments.Domain.Deployment;
using Aethra.Modules.Deployments.Infrastructure;
using Aethra.Modules.Mcp.Security;
using Aethra.Shared.Contracts.Deployments;
using Aethra.Shared.Kernel.Ids;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Herramientas MCP de alto nivel alineadas con el modelo operacional:
/// App Environment -> Release -> Machine.
/// </summary>
[McpServerToolType]
public sealed class OperationalWorkflowTools(
    IMediator mediator,
    DeploymentsDbContext deploymentsDb,
    IMcpCallerContext caller)
{
    [McpServerTool(Name = "aethra_deploy_app_environment", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Dispara un deploy nativo de un App Environment en background. Usa la unidad mental operacional, aunque internamente el id sea una Instance.")]
    public async Task<object> DeployAppEnvironmentAsync(
        [Description("ID del App Environment / Instance (formato 'ins_...').")] string appEnvironmentId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.DeploymentsTrigger))
        {
            return McpResponses.InsufficientScope(McpScopes.DeploymentsTrigger);
        }

        await mediator.Publish(new NativeRedeployRequestedIntegrationEvent(appEnvironmentId, "mcp"), ct).ConfigureAwait(false);
        return McpResponses.OkWithNextActions(
            new
            {
                app_environment_id = appEnvironmentId,
                status = "queued",
                note = "Deploy nativo corriendo en background."
            },
            [
                new McpResponses.NextAction(
                    "aethra_explain_failed_deploy",
                    "Si el deploy falla, usa esta tool con el deployment_id reportado por la UI/API para obtener causa y siguientes pasos.",
                    new { deployment_id = "dep_..." })
            ]);
    }

    [McpServerTool(Name = "aethra_explain_failed_deploy", ReadOnly = true, OpenWorld = false)]
    [Description("Explica un deployment fallido o degradado con estado, etapa, errores, build asociado y ultimas lineas de logs.")]
    public async Task<object> ExplainFailedDeployAsync(
        [Description("ID del deployment (formato 'dep_...').")] string deploymentId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.DeploymentsRead))
        {
            return McpResponses.InsufficientScope(McpScopes.DeploymentsRead);
        }
        if (!TryDeploymentId(deploymentId, out var typedDeploymentId))
        {
            return McpResponses.Failure("deployment.invalid_id", "DeploymentId invalido. Debe tener formato dep_...", "validation");
        }

        var deployment = await deploymentsDb.Deployments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == typedDeploymentId, ct)
            .ConfigureAwait(false);
        if (deployment is null)
        {
            return McpResponses.Failure("deployment.not_found", $"Deployment '{deploymentId}' no existe.", "not_found");
        }

        var deploymentLogs = await deploymentsDb.DeploymentLogs.AsNoTracking()
            .Where(l => l.DeploymentId == typedDeploymentId)
            .OrderByDescending(l => l.Sequence)
            .Take(20)
            .Select(l => new
            {
                l.Sequence,
                Timestamp = l.Timestamp,
                Level = l.Level.ToString(),
                l.Stage,
                l.Text
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        object? buildSummary = null;
        IReadOnlyList<object> buildLogs = [];
        if (TryBuildId(deployment.BuildId, out var typedBuildId))
        {
            var build = await deploymentsDb.Builds.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == typedBuildId, ct)
                .ConfigureAwait(false);
            if (build is not null)
            {
                buildSummary = new
                {
                    id = build.Id.ToString(),
                    status = build.Status.ToString(),
                    failed_at_stage = build.FailedAtStage?.ToString(),
                    error_code = build.ErrorCode,
                    error_message = build.ErrorMessage,
                    image_ref = build.ImageRef
                };
                buildLogs = await deploymentsDb.BuildLogs.AsNoTracking()
                    .Where(l => l.BuildId == typedBuildId)
                    .OrderByDescending(l => l.Sequence)
                    .Take(12)
                    .Select(l => new
                    {
                        l.Sequence,
                        Timestamp = l.Timestamp,
                        Level = l.Level.ToString(),
                        l.Stage,
                        l.Text
                    })
                    .Cast<object>()
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
            }
        }

        return McpResponses.OkWithNextActions(
            new
            {
                deployment = new
                {
                    id = deployment.Id.ToString(),
                    build_id = deployment.BuildId,
                    app_environment_id = deployment.InstanceId,
                    status = deployment.Status.ToString(),
                    failed_at_stage = deployment.FailedAtStage?.ToString(),
                    error_code = deployment.ErrorCode,
                    error_message = deployment.ErrorMessage,
                    image_ref = deployment.NewImageRef,
                    created_at = deployment.CreatedAt,
                    started_at = deployment.StartedAt,
                    finished_at = deployment.FinishedAt
                },
                build = buildSummary,
                recent_deployment_logs = deploymentLogs.OrderBy(l => l.Sequence),
                recent_build_logs = buildLogs
            },
            SuggestedActions(deployment));
    }

    private static IReadOnlyList<McpResponses.NextAction> SuggestedActions(Aethra.Modules.Deployments.Domain.Deployment.Deployment deployment)
    {
        if (deployment.Status == DeploymentStatus.Completed)
        {
            return
            [
                new McpResponses.NextAction(
                    "aethra_deploy_app_environment",
                    "El deployment esta completado; usa redeploy solo si necesitas recrear contenedores con la config actual.",
                    new { app_environment_id = deployment.InstanceId })
            ];
        }

        return
        [
            new McpResponses.NextAction(
                "aethra_deploy_app_environment",
                "Reintenta el App Environment despues de corregir la causa raiz indicada por logs/error_code.",
                new { app_environment_id = deployment.InstanceId })
        ];
    }

    private static bool TryDeploymentId(string raw, out DeploymentId id)
    {
        id = default;
        if (!AethraId.TryParse(raw, out var parsed) || parsed.Value.Prefix != "dep")
        {
            return false;
        }
        id = new DeploymentId(parsed.Value);
        return true;
    }

    private static bool TryBuildId(string raw, out BuildId id)
    {
        id = default;
        if (!AethraId.TryParse(raw, out var parsed) || parsed.Value.Prefix != "bld")
        {
            return false;
        }
        id = new BuildId(parsed.Value);
        return true;
    }
}
