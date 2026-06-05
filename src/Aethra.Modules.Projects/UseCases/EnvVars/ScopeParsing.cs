using Aethra.Modules.Projects.Domain.EnvVars;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Projects.UseCases.EnvVars;

/// <summary>
/// Helper compartido por los Queries/Commands de env vars y secrets para traducir el
/// discriminador textual de scope (<c>project|template|client|instance</c>) que llega por la API
/// al enum de dominio <see cref="EnvScopeType"/>. Centraliza la validación para devolver siempre
/// el mismo <see cref="Error.Validation"/> ante un scope desconocido.
/// </summary>
internal static class ScopeParsing
{
    public static Result<EnvScopeType> ParseScopeType(string? scopeType)
    {
        if (!Enum.TryParse<EnvScopeType>(scopeType, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            return Error.Validation(
                "env_scope.invalid",
                $"scopeType='{scopeType}' inválido. Use project, template, client o instance.");
        }
        return Result.Success(parsed);
    }
}
