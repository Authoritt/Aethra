using Aethra.Modules.Settings.Domain;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Settings.UseCases;

internal static class IdParsing
{
    public static Result<IntegrationCredentialId> ParseCredentialId(string? raw)
    {
        if (!AethraId.TryParse(raw, out var parsed) || parsed.Value.Prefix != "int")
        {
            return Error.Validation("settings.invalid_credential_id", "ID de credencial inválido.");
        }
        return Result.Success(new IntegrationCredentialId(parsed.Value));
    }

    public static Result<BaseDomainId> ParseBaseDomainId(string? raw)
    {
        if (!AethraId.TryParse(raw, out var parsed) || parsed.Value.Prefix != "bd")
        {
            return Error.Validation("settings.invalid_base_domain_id", "ID de base domain inválido.");
        }
        return Result.Success(new BaseDomainId(parsed.Value));
    }

    public static Result<EnvironmentDefinitionId> ParseEnvironmentDefinitionId(string? raw)
    {
        if (!AethraId.TryParse(raw, out var parsed) || parsed.Value.Prefix != "envd")
        {
            return Error.Validation("settings.invalid_environment_id", "ID de ambiente inválido.");
        }
        return Result.Success(new EnvironmentDefinitionId(parsed.Value));
    }
}
