using Aethra.Modules.Projects.Domain.Templates;
using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Templates.Commands;

/// <summary>
/// Rota el webhook secret del Template. Devuelve el nuevo secret en plain UNA vez (como el create);
/// el anterior queda inválido. El frontend (RotateWebhookSecretButton) consume este endpoint.
/// </summary>
public sealed record RotateWebhookSecretCommand(string TemplateId) : ICommand<RotateWebhookSecretResult>;

public sealed record RotateWebhookSecretResult(string TemplateId, string WebhookSecret);

internal sealed class RotateWebhookSecretHandler(ProjectsDbContext db, IWebhookSecretCodec codec, IClock clock)
    : ICommandHandler<RotateWebhookSecretCommand, RotateWebhookSecretResult>
{
    public async Task<Result<RotateWebhookSecretResult>> Handle(RotateWebhookSecretCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.TemplateId, out var parsed) || parsed.Value.Prefix != "tpl")
        {
            return Error.Validation("template.invalid_id", "ID de template inválido.");
        }
        var templateId = new TemplateId(parsed.Value);

        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound("template.not_found", $"Template '{request.TemplateId}' no existe.");
        }

        var newSecret = template.RotateWebhookSecret(codec, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new RotateWebhookSecretResult(template.Id.ToString(), newSecret);
    }
}
