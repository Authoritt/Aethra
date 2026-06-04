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
/// F13 — define la topología de servicios multi-contenedor de un Template (reemplaza el set).
/// </summary>
public sealed record SetTemplateServicesCommand(
    string TemplateId,
    IReadOnlyList<TemplateServiceInput> Services) : ICommand;

public sealed record TemplateServiceInput(
    string Name,
    string Image,
    int Port,
    IReadOnlyList<string>? PathPrefixes,
    IReadOnlyDictionary<string, string>? Env,
    string? BuildMode = null,
    string? DockerfilePath = null);

internal sealed class SetTemplateServicesHandler(ProjectsDbContext db, IClock clock)
    : ICommandHandler<SetTemplateServicesCommand>
{
    public async Task<Result> Handle(SetTemplateServicesCommand request, CancellationToken cancellationToken)
    {
        if (!AethraId.TryParse(request.TemplateId, out var parsed) || parsed.Value.Prefix != "tpl")
        {
            return Error.Validation("template.invalid_id", "ID de template inválido.");
        }
        var templateId = new TemplateId(parsed.Value);

        var all = await db.Templates.ToListAsync(cancellationToken).ConfigureAwait(false);
        var template = all.FirstOrDefault(t => t.Id == templateId);
        if (template is null)
        {
            return Error.NotFound("template.not_found", $"Template '{request.TemplateId}' no existe.");
        }

        var services = (request.Services ?? [])
            .Select(s => new TemplateService(
                Name: s.Name.Trim(),
                Image: s.Image.Trim(),
                Port: s.Port,
                PathPrefixes: s.PathPrefixes?.ToList() ?? [],
                Env: (s.Env ?? new Dictionary<string, string>())
                    .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)).ToList(),
                BuildMode: string.IsNullOrWhiteSpace(s.BuildMode) ? "registry" : s.BuildMode.Trim().ToLowerInvariant(),
                DockerfilePath: s.DockerfilePath?.Trim()))
            .ToList();

        template.ReplaceServices(services, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
