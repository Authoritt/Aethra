using Aethra.Modules.Identity.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.UseCases.Commands;

public sealed record RevokeApiKeyCommand(string ApiKeyId) : ICommand;

internal sealed class RevokeApiKeyHandler(IdentityDbContext db, IClock clock)
    : ICommandHandler<RevokeApiKeyCommand>
{
    public async Task<Result> Handle(RevokeApiKeyCommand request, CancellationToken cancellationToken)
    {
        var apiKey = await db.ApiKeys.FirstOrDefaultAsync(
            k => k.Id.ToString() == request.ApiKeyId, cancellationToken);
        if (apiKey is null)
        {
            return Error.NotFound("api_key.not_found", $"API key '{request.ApiKeyId}' no existe.");
        }
        apiKey.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
