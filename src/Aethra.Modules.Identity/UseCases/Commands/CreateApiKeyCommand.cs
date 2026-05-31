using Aethra.Modules.Identity.Domain;
using Aethra.Modules.Identity.Infrastructure;
using Aethra.Modules.Identity.UseCases.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using FluentValidation;

namespace Aethra.Modules.Identity.UseCases.Commands;

public sealed record CreateApiKeyCommand(
    string Name,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt) : ICommand<CreateApiKeyResultDto>;

public sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKeyCommand>
{
    public CreateApiKeyValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Scopes).NotEmpty().WithMessage("Una API key requiere al menos un scope.");
    }
}

internal sealed class CreateApiKeyHandler(IdentityDbContext db, IApiKeyHasher hasher, IClock clock)
    : ICommandHandler<CreateApiKeyCommand, CreateApiKeyResultDto>
{
    public async Task<Result<CreateApiKeyResultDto>> Handle(CreateApiKeyCommand request, CancellationToken cancellationToken)
    {
        var plainSecret = ApiKeyGenerator.Generate();

        ApiKey apiKey;
        try
        {
            apiKey = ApiKey.Create(request.Name, plainSecret, request.Scopes, clock.UtcNow, hasher, request.ExpiresAt);
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("api_key.invalid", ex.Message);
        }

        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateApiKeyResultDto(
            Id: apiKey.Id.ToString(),
            Name: apiKey.Name,
            KeyPrefix: apiKey.KeyPrefix,
            Scopes: [.. apiKey.Scopes],
            CreatedAt: apiKey.CreatedAt,
            ExpiresAt: apiKey.ExpiresAt,
            Secret: plainSecret);
    }
}
