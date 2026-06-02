using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
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
        // Comparamos por el wrapper tipado (ApiKeyId) que SI traduce a SQL con el
        // ValueConverter activo. Eso evita materializar toda la tabla en memoria.
        if (!AethraId.TryParse(request.ApiKeyId, out var parsed) || parsed.Value.Prefix != "apk")
        {
            return Error.NotFound("api_key.not_found", $"API key '{request.ApiKeyId}' no existe.");
        }
        var typedId = new ApiKeyId(parsed.Value);

        var apiKey = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == typedId, cancellationToken);
        if (apiKey is null)
        {
            return Error.NotFound("api_key.not_found", $"API key '{request.ApiKeyId}' no existe.");
        }
        apiKey.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
