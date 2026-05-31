using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Proxy.Infrastructure.Yarp;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Proxy.UseCases.Routes.Commands;

public sealed record DeleteRouteCommand(string RouteId) : ICommand;

internal sealed class DeleteRouteHandler(ProxyDbContext db, IClock clock, IProxyConfigService config)
    : ICommandHandler<DeleteRouteCommand>
{
    public async Task<Result> Handle(DeleteRouteCommand request, CancellationToken cancellationToken)
    {
        _ = clock;
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

        route.MarkRemoved();
        db.Routes.Remove(route);
        await db.SaveChangesAsync(cancellationToken);
        config.Reload();
        return Result.Success();
    }
}
