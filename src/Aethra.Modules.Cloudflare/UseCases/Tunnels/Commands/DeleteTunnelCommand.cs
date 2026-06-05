using Aethra.Modules.Cloudflare.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Cloudflare.UseCases.Tunnels.Commands;

/// <summary>
/// F13.9 — borra el túnel gestionado del registro de Aethra. NO toca la config remota de
/// Cloudflare (el túnel sigue existiendo en CF); solo desvincula a Aethra de su gestión.
/// Si <see cref="TunnelId"/> es null, borra el túnel singleton (el primero registrado).
/// </summary>
public sealed record DeleteTunnelCommand(string? TunnelId = null) : ICommand;

internal sealed class DeleteTunnelHandler(CloudflareDbContext db)
    : ICommandHandler<DeleteTunnelCommand>
{
    public async Task<Result> Handle(DeleteTunnelCommand request, CancellationToken cancellationToken)
    {
        var tunnel = string.IsNullOrWhiteSpace(request.TunnelId)
            ? await db.Tunnels.OrderBy(t => t.CreatedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : await db.Tunnels.FirstOrDefaultAsync(t => t.TunnelId == request.TunnelId.Trim(), cancellationToken).ConfigureAwait(false);

        if (tunnel is null)
        {
            return Error.NotFound("tunnel.none", "No hay túnel gestionado registrado.");
        }

        db.Tunnels.Remove(tunnel);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
