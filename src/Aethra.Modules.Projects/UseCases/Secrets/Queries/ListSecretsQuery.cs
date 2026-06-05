using Aethra.Modules.Projects.Infrastructure;
using Aethra.Modules.Projects.UseCases.EnvVars;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Projects.UseCases.Secrets.Queries;

/// <summary>
/// Lista los secretos de un scope polimórfico cualquiera (project|template|client|instance).
/// SEGURIDAD: NUNCA devuelve el valor descifrado ni el cipher — solo metadata (key, source,
/// timestamps) y <see cref="SecretDto.HasValue"/>. El plaintext solo se descifra en el
/// orquestador de deploy.
/// </summary>
public sealed record ListSecretsQuery(string ScopeType, string ScopeId)
    : IQuery<IReadOnlyList<SecretDto>>;

/// <summary>
/// DTO de lectura de un secreto. NO contiene el valor (ni plaintext ni cipher);
/// <see cref="HasValue"/> indica únicamente que existe un cipher persistido.
/// </summary>
public sealed record SecretDto(
    string Key,
    bool HasValue,
    string? Source,
    string ScopeType,
    string ScopeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed class ListSecretsHandler(ProjectsDbContext db)
    : IQueryHandler<ListSecretsQuery, IReadOnlyList<SecretDto>>
{
    public async Task<Result<IReadOnlyList<SecretDto>>> Handle(
        ListSecretsQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = ScopeParsing.ParseScopeType(request.ScopeType);
        if (scopeResult.IsFailure)
        {
            return scopeResult.Error;
        }
        var scopeType = scopeResult.Value;
        var scopeId = request.ScopeId ?? string.Empty;

        // Proyectamos en BD sin traer el ValueCipher: el plaintext/cipher jamás sale del módulo.
        var rows = await db.Secrets
            .AsNoTracking()
            .Where(s => s.ScopeType == scopeType && s.ScopeId == scopeId)
            .OrderBy(s => s.Key)
            .Select(s => new
            {
                s.Key,
                HasValue = s.ValueCipher.Length > 0,
                s.Source,
                s.ScopeType,
                s.ScopeId,
                s.CreatedAt,
                s.UpdatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SecretDto> dtos = [.. rows.Select(s => new SecretDto(
            Key: s.Key,
            HasValue: s.HasValue,
            Source: s.Source,
            ScopeType: s.ScopeType.ToString(),
            ScopeId: s.ScopeId,
            CreatedAt: s.CreatedAt,
            UpdatedAt: s.UpdatedAt))];

        return Result.Success(dtos);
    }
}
