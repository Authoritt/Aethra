using Aethra.Modules.Services.Templates;
using Aethra.Modules.Services.UseCases.Dtos;
using Aethra.Modules.Services.UseCases.Mapping;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Services.UseCases.Queries;

public sealed record ListTemplatesQuery() : IQuery<IReadOnlyList<ServiceTemplateDto>>;

internal sealed class ListTemplatesHandler(IServiceTemplateCatalog catalog)
    : IQueryHandler<ListTemplatesQuery, IReadOnlyList<ServiceTemplateDto>>
{
    public Task<Result<IReadOnlyList<ServiceTemplateDto>>> Handle(ListTemplatesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ServiceTemplateDto> dtos = [.. catalog.GetAll().Select(ServiceMappers.ToDto)];
        return Task.FromResult(Result.Success(dtos));
    }
}
