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
/// F12.3 — reemplaza el set completo de <see cref="TemplateEnvironmentMapping"/> de un Template.
/// </summary>
public sealed record SetEnvironmentMappingCommand(
    string TemplateId,
    IReadOnlyList<EnvironmentMappingItemDto> Mappings) : ICommand;

public sealed record EnvironmentMappingItemDto(string environment, string branch);

internal sealed class SetEnvironmentMappingHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<SetEnvironmentMappingCommand>
{
    public async Task<Result> Handle(SetEnvironmentMappingCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.TemplateId, out var parsed) || parsed.Value.Prefix != "tpl")
        {
            return Error.Validation("template.invalid_id", "ID de template inválido.");
        }
        var typedId = new TemplateId(parsed.Value);
        var template = await db.Templates
            .Include(t => t.EnvironmentMapping)
            .FirstOrDefaultAsync(t => t.Id == typedId, cancellationToken)
            .ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound("template.not_found", $"Template '{request.TemplateId}' no existe.");
        }

        IEnumerable<TemplateEnvironmentMapping> entities;
        try
        {
            entities = request.Mappings.Select(m => new TemplateEnvironmentMapping(m.environment, m.branch));
        }
        catch (ArgumentException ex)
        {
            return Error.Validation("template.invalid_environment_mapping", ex.Message);
        }
        template.ReplaceEnvironmentMapping(entities, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
