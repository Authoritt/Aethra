using Aethra.Modules.Cloudflare.Application.Dtos;
using Aethra.Modules.Cloudflare.UseCases.DnsRecords.Commands;
using Aethra.Modules.Cloudflare.UseCases.Zones.Queries;
using Aethra.Modules.Monitoring.UseCases.Commands;
using Aethra.Modules.Monitoring.UseCases.Dtos;
using Aethra.Modules.Proxy.UseCases.Dtos;
using Aethra.Modules.Proxy.UseCases.Routes.Commands;
using Aethra.Shared.Contracts.Projects;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Mcp.UseCases;

/// <summary>
/// Adjunta un dominio a una Instance: crea el DNS record en Cloudflare (CNAME proxied),
/// crea la Route YARP y opcionalmente un Monitor HTTP. Resultado idempotente best-effort:
/// si el record ya existe, no aborta el route; si el route ya existe, no aborta el monitor.
///
/// Vive en Modules.Mcp porque combina 3 módulos — exactamente el caso de uso que justifica
/// que el módulo Mcp pueda referenciar otros.
/// </summary>
public sealed record AttachDomainCommand(
    string InstanceId,
    string Hostname,
    string? CloudflareZoneId,
    bool CreateMonitor) : ICommand<AttachDomainResult>;

public sealed record AttachDomainResult(
    string InstanceId,
    string Hostname,
    AttachDomainStepResult Dns,
    AttachDomainStepResult Route,
    AttachDomainStepResult Monitor);

public sealed record AttachDomainStepResult(
    bool Skipped,
    bool Success,
    string? Id,
    string? ErrorCode,
    string? ErrorMessage);

internal sealed class AttachDomainHandler(
    IMediator mediator,
    IInstanceLookup instanceLookup,
    ILogger<AttachDomainHandler> logger)
    : ICommandHandler<AttachDomainCommand, AttachDomainResult>
{
    public async Task<Result<AttachDomainResult>> Handle(AttachDomainCommand request, CancellationToken cancellationToken)
    {
        var instance = await instanceLookup.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
        if (instance is null)
        {
            return Error.NotFound("instance.not_found", $"Instance '{request.InstanceId}' no existe.");
        }

        var hostname = request.Hostname.Trim().ToLowerInvariant();

        // -------- Paso 1: DNS record en Cloudflare --------
        AttachDomainStepResult dnsStep;
        string? cloudflareZoneId = request.CloudflareZoneId;
        if (string.IsNullOrWhiteSpace(cloudflareZoneId))
        {
            cloudflareZoneId = await ResolveZoneIdByHostnameAsync(hostname, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(cloudflareZoneId))
        {
            dnsStep = new AttachDomainStepResult(
                Skipped: true,
                Success: false,
                Id: null,
                ErrorCode: "cloudflare.zone_unresolved",
                ErrorMessage: "No se especificó cloudflare_zone_id y no se pudo inferir del hostname.");
        }
        else
        {
            var dnsCmd = new CreateDnsRecordCommand(
                ZoneId: cloudflareZoneId!,
                Type: "CNAME",
                Name: hostname,
                Content: "proxy.aethra.local",  // CNAME al proxy — el operador puede ajustarlo después.
                Ttl: 1,                         // 1 = "auto" en Cloudflare cuando proxied=true.
                Proxied: true,
                Comment: $"attach_domain by mcp for instance {instance.InstanceId}");

            var dnsResult = await mediator.Send(dnsCmd, cancellationToken).ConfigureAwait(false);
            dnsStep = dnsResult.IsSuccess
                ? new AttachDomainStepResult(false, true, dnsResult.Value.Id, null, null)
                : new AttachDomainStepResult(false, false, null, dnsResult.Error.Code, dnsResult.Error.Message);

            if (!dnsResult.IsSuccess)
            {
                logger.LogWarning("attach_domain: DNS step falló para {Host}: {Code} {Msg}",
                    hostname, dnsResult.Error.Code, dnsResult.Error.Message);
            }
        }

        // -------- Paso 2: Route YARP --------
        var backendUrl = BuildBackendUrl(instance);
        var routeCmd = new CreateRouteCommand(hostname, backendUrl, TlsEnabled: true);
        var routeResult = await mediator.Send(routeCmd, cancellationToken).ConfigureAwait(false);
        var routeStep = routeResult.IsSuccess
            ? new AttachDomainStepResult(false, true, routeResult.Value.Id, null, null)
            : new AttachDomainStepResult(false, false, null, routeResult.Error.Code, routeResult.Error.Message);

        if (!routeResult.IsSuccess)
        {
            logger.LogWarning("attach_domain: route step falló para {Host}: {Code} {Msg}",
                hostname, routeResult.Error.Code, routeResult.Error.Message);
        }

        // -------- Paso 3: Monitor HTTP (opcional) --------
        AttachDomainStepResult monitorStep;
        if (!request.CreateMonitor)
        {
            monitorStep = new AttachDomainStepResult(Skipped: true, Success: false, Id: null, ErrorCode: null, ErrorMessage: null);
        }
        else
        {
            var monitorSlug = SafeSlug(hostname);
            var monitorCmd = new CreateMonitorCommand(
                Slug: monitorSlug,
                Name: $"Health: {hostname}",
                Url: $"https://{hostname}/",
                HttpMethod: "GET",
                ExpectedStatusCodes: [200, 204, 301, 302],
                IntervalSec: 60,
                TimeoutMs: 10000,
                Headers: null,
                BodyTemplate: null,
                InstanceId: instance.InstanceId,
                ProjectId: instance.ProjectId);
            var monitorResult = await mediator.Send(monitorCmd, cancellationToken).ConfigureAwait(false);
            monitorStep = monitorResult.IsSuccess
                ? new AttachDomainStepResult(false, true, monitorResult.Value.Id, null, null)
                : new AttachDomainStepResult(false, false, null, monitorResult.Error.Code, monitorResult.Error.Message);
        }

        return new AttachDomainResult(
            InstanceId: instance.InstanceId,
            Hostname: hostname,
            Dns: dnsStep,
            Route: routeStep,
            Monitor: monitorStep);
    }

    /// <summary>
    /// Intenta resolver la zona buscando una cuyos <c>Name</c> sea sufijo del hostname.
    /// Devuelve null si no hay zonas registradas o ninguna coincide.
    /// </summary>
    private async Task<string?> ResolveZoneIdByHostnameAsync(string hostname, CancellationToken ct)
    {
        var zones = await mediator.Send(new ListZonesQuery(), ct).ConfigureAwait(false);
        if (!zones.IsSuccess)
        {
            return null;
        }
        CloudflareZoneDto? match = null;
        foreach (var z in zones.Value)
        {
            if (hostname.EndsWith("." + z.Name, StringComparison.OrdinalIgnoreCase)
                || hostname.Equals(z.Name, StringComparison.OrdinalIgnoreCase))
            {
                // Si hay varias coincidencias, gana la de nombre más largo (más específica).
                if (match is null || z.Name.Length > match.Name.Length)
                {
                    match = z;
                }
            }
        }
        return match?.Id;
    }

    private static string BuildBackendUrl(InstanceForDeployView instance)
    {
        // Convención: el satélite expone el contenedor en su VM en el puerto declarado.
        // Si no hay primary port, asumimos 8080 (default razonable).
        var port = instance.PrimaryContainerPort ?? 8080;
        return $"http://{instance.ContainerName}:{port}";
    }

    private static string SafeSlug(string hostname)
    {
        var chars = hostname.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        var raw = new string(chars).Trim('-');
        if (raw.Length > 60)
        {
            raw = raw[..60];
        }
        return string.IsNullOrEmpty(raw) ? "monitor" : raw;
    }
}
