using Aethra.Modules.Cloudflare.Domain;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Ids;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Cloudflare.UseCases;

internal static class IdParsing
{
    public static Result<CloudflareZoneId> ParseZoneId(string? raw)
    {
        if (!AethraId.TryParse(raw, out var parsed) || parsed.Value.Prefix != "cfz")
        {
            return Error.Validation("cloudflare.invalid_zone_id", "ID de zona invalido.");
        }
        return Result.Success(new CloudflareZoneId(parsed.Value));
    }

    public static Result<DnsRecordId> ParseRecordId(string? raw)
    {
        if (!AethraId.TryParse(raw, out var parsed) || parsed.Value.Prefix != "cfr")
        {
            return Error.Validation("cloudflare.invalid_record_id", "ID de record DNS invalido.");
        }
        return Result.Success(new DnsRecordId(parsed.Value));
    }
}
