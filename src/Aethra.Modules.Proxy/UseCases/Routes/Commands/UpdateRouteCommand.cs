using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Proxy.Infrastructure.Yarp;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Proxy.UseCases.Routes.Commands;

/// <summary>
/// Actualiza el backend y el flag TLS de una ruta existente. Hostname/pathPrefix son inmutables
/// (identifican la ruta) — para cambiarlos hay que borrar y recrear.
/// </summary>
public sealed record UpdateRouteCommand(
    string RouteId,
    string BackendUrl,
    bool TlsEnabled,
    string? OperationalOwnerType = null,
    string? OperationalOwnerId = null,
    string? Origin = null) : ICommand;

public sealed class UpdateRouteValidator : AbstractValidator<UpdateRouteCommand>
{
    public UpdateRouteValidator()
    {
        RuleFor(c => c.BackendUrl).NotEmpty().MaximumLength(512);
    }
}

internal sealed class UpdateRouteHandler(ProxyDbContext db, IClock clock, IProxyConfigService config)
    : ICommandHandler<UpdateRouteCommand>
{
    public async Task<Result> Handle(UpdateRouteCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.RouteId, out var parsed) || parsed.Value.Prefix != "rt")
        {
            return Error.Validation("route.invalid_id", "ID de ruta inválido.");
        }
        var typedId = new RouteId(parsed.Value);

        var route = await db.Routes.FirstOrDefaultAsync(r => r.Id == typedId, cancellationToken);
        if (route is null)
        {
            return Error.NotFound("route.not_found", $"Ruta '{request.RouteId}' no existe.");
        }

        var now = clock.UtcNow;
        try
        {
            route.UpdateBackend(request.BackendUrl, now);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("route.invalid_backend", ex.Message);
        }
        // null cert: el TLS por LE se resuelve por DNS-01 wildcard; no atamos cert específico acá.
        route.SetTls(request.TlsEnabled, null, now);
        if (request.OperationalOwnerType is not null || request.OperationalOwnerId is not null || request.Origin is not null)
        {
            route.SetOperationalOwner(request.OperationalOwnerType, request.OperationalOwnerId, request.Origin, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Hot-reload YARP sin restart.
        config.Reload();

        return Result.Success();
    }
}
