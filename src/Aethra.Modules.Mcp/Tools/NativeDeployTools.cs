using System.ComponentModel;
using Aethra.Modules.Mcp.Security;
using Aethra.Modules.Projects.UseCases.Templates.Commands;
using Aethra.Shared.Contracts.Deployments;
using MediatR;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// F13 — herramientas MCP del deploy nativo multi-servicio: definir la topología de servicios de
/// un Template y disparar el deploy nativo de una Instance (un contenedor por servicio + rutas).
/// </summary>
[McpServerToolType]
public sealed class NativeDeployTools(IMediator mediator, IMcpCallerContext caller)
{
    public sealed record McpTemplateService(
        [property: Description("Nombre del servicio (ej. 'backend', 'frontend'). Define el nombre del contenedor {instance}-{name}.")]
        string Name,
        [property: Description("Imagen prebuilt (modo registry), ej. 'ghcr.io/org/app:tag'. Vacío si buildMode='git'.")]
        string Image,
        [property: Description("Puerto interno del contenedor.")]
        int Port,
        [property: Description("Prefijos de ruta que sirve este servicio (ej. ['/api','/hubs'] o ['/']). Vacío = interno sin ruta pública.")]
        IReadOnlyList<string>? PathPrefixes,
        [property: Description("Env extra del servicio. El token {instance} se interpola al slug. Ej. API_BASE_URL=http://{instance}-backend:5006.")]
        IReadOnlyDictionary<string, string>? Env,
        [property: Description("Modo de build: 'registry' (pull de Image) o 'git' (Aethra clona+construye DockerfilePath). Default 'registry'.")]
        string? BuildMode,
        [property: Description("Solo modo 'git': ruta al Dockerfile del servicio dentro del repo. Default 'Dockerfile'.")]
        string? DockerfilePath,
        [property: Description("Volúmenes persistentes del servicio (ej. DataProtection keys). El token {instance} en el nombre se interpola al slug.")]
        IReadOnlyList<McpServiceVolume>? Volumes);

    public sealed record McpServiceVolume(
        [property: Description("Nombre del named volume. Admite {instance} → slug (ej. '{instance}-dpkeys').")]
        string Name,
        [property: Description("Ruta de montaje dentro del contenedor (ej. '/app/dp-keys').")]
        string ContainerPath,
        [property: Description("Montar de solo lectura.")]
        bool ReadOnly);

    [McpServerTool(Name = "aethra_set_template_services", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Define la topología de servicios multi-contenedor de un Template (reemplaza el set). Cada servicio puede construirse desde git o usar una imagen prebuilt de registry.")]
    public async Task<object> SetServicesAsync(
        [Description("ID del template (formato 'tpl_...').")] string templateId,
        [Description("Lista de servicios del template.")] IReadOnlyList<McpTemplateService> services,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.ProjectsWrite))
        {
            return McpResponses.InsufficientScope(McpScopes.ProjectsWrite);
        }
        var mapped = (services ?? [])
            .Select(s => new TemplateServiceInput(
                s.Name, s.Image, s.Port, s.PathPrefixes, s.Env, s.BuildMode, s.DockerfilePath,
                s.Volumes?.Select(v => new TemplateVolumeInput(v.Name, v.ContainerPath, v.ReadOnly)).ToList()))
            .ToList();
        var result = await mediator.Send(new SetTemplateServicesCommand(templateId, mapped), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? McpResponses.Ok(new { templateId, services = mapped.Count })
            : McpResponses.FromError(result.Error);
    }

    [McpServerTool(Name = "aethra_deploy_instance_native", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Dispara el deploy NATIVO multi-contenedor de una Instance en background (un contenedor por servicio del template, build-from-git/registry, healthcheck + rutas). Devuelve al instante; el deploy corre async.")]
    public async Task<object> DeployAsync(
        [Description("ID de la instance (formato 'ins_...').")] string instanceId,
        CancellationToken ct)
    {
        if (!caller.HasScope(McpScopes.DeploymentsTrigger))
        {
            return McpResponses.InsufficientScope(McpScopes.DeploymentsTrigger);
        }
        await mediator.Publish(new NativeRedeployRequestedIntegrationEvent(instanceId, "mcp"), ct).ConfigureAwait(false);
        return McpResponses.Ok(new { instanceId, status = "queued", note = "Deploy nativo corriendo en background." });
    }
}
