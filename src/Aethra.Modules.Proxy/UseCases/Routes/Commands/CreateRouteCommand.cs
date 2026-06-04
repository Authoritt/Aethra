using Aethra.Modules.Proxy.Domain;
using Aethra.Modules.Proxy.Infrastructure;
using Aethra.Modules.Proxy.Infrastructure.Yarp;
using Aethra.Modules.Proxy.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Primitives;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Proxy.UseCases.Routes.Commands;

public sealed record CreateRouteCommand(string Hostname, string BackendUrl, bool TlsEnabled, string? PathPrefix = null) : ICommand<RouteDto>;

public sealed class CreateRouteValidator : AbstractValidator<CreateRouteCommand>
{
    public CreateRouteValidator()
    {
        RuleFor(c => c.Hostname).NotEmpty().MaximumLength(253);
        RuleFor(c => c.BackendUrl).NotEmpty().MaximumLength(512);
    }
}

internal sealed class CreateRouteHandler(ProxyDbContext db, IClock clock, IProxyConfigService config)
    : ICommandHandler<CreateRouteCommand, RouteDto>
{
    public async Task<Result<RouteDto>> Handle(CreateRouteCommand request, CancellationToken cancellationToken)
    {
        var hostnameResult = Hostname.Create(request.Hostname);
        if (hostnameResult.IsFailure)
        {
            return hostnameResult.Error;
        }
        var hostname = hostnameResult.Value;
        var pathPrefix = Route.NormalizePathPrefix(request.PathPrefix);

        if (await db.Routes.AnyAsync(r => r.Hostname == hostname && r.PathPrefix == pathPrefix, cancellationToken))
        {
            return Error.Conflict("route.hostname_taken", $"Ya existe una ruta para '{hostname}' con path '{pathPrefix}'.");
        }

        Route route;
        try
        {
            route = Route.Create(hostname, pathPrefix, request.BackendUrl, request.TlsEnabled, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("route.invalid_backend", ex.Message);
        }

        db.Routes.Add(route);
        await db.SaveChangesAsync(cancellationToken);

        // Hot-reload YARP sin restart.
        config.Reload();

        return RouteMapper.ToDto(route, certStatus: route.TlsEnabled ? "pending" : "none", certExpiresAt: null);
    }
}
