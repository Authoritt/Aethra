using Aethra.Modules.Projects.Infrastructure;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.EnvVars.Queries;

/// <summary>
/// Lista las env vars (no secretas) de un scope polimórfico cualquiera
/// (project|template|client|instance) identificado por (<paramref name="ScopeType"/>,
/// <paramref name="ScopeId"/>). Devuelve los valores en claro: las env vars planas NO son
/// secretas por diseño (los secretos viven en otra tabla y nunca exponen su valor).
/// </summary>
public sealed record ListEnvVarsQuery(string ScopeType, string ScopeId)
    : IQuery<IReadOnlyList<EnvVarDto>>;

/// <summary>DTO de lectura de una env var. Incluye valor + flags + timestamps.</summary>
public sealed record EnvVarDto(
    string Key,
    string Value,
    bool IsBuildTime,
    bool IsRuntime,
    bool IsLiteral,
    bool IsMultiline,
    string? Source,
    string ScopeType,
    string ScopeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed class ListEnvVarsHandler(ProjectsDbContext db)
    : IQueryHandler<ListEnvVarsQuery, IReadOnlyList<EnvVarDto>>
{
    public async Task<Result<IReadOnlyList<EnvVarDto>>> Handle(
        ListEnvVarsQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = ScopeParsing.ParseScopeType(request.ScopeType);
        if (scopeResult.IsFailure)
        {
            return scopeResult.Error;
        }
        var scopeType = scopeResult.Value;
        var scopeId = request.ScopeId ?? string.Empty;

        var rows = await db.EnvironmentVariables
            .AsNoTracking()
            .Where(e => e.ScopeType == scopeType && e.ScopeId == scopeId)
            .OrderBy(e => e.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<EnvVarDto> dtos = [.. rows.Select(e => new EnvVarDto(
            Key: e.Key,
            Value: e.Value,
            IsBuildTime: e.IsBuildTime,
            IsRuntime: e.IsRuntime,
            IsLiteral: e.IsLiteral,
            IsMultiline: e.IsMultiline,
            Source: e.Source,
            ScopeType: e.ScopeType.ToString(),
            ScopeId: e.ScopeId,
            CreatedAt: e.CreatedAt,
            UpdatedAt: e.UpdatedAt))];

        return Result.Success(dtos);
    }
}
