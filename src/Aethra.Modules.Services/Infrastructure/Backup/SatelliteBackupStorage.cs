using Aethra.Shared.Contracts.Containers;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// Storage de backups en el disco de un SATÉLITE con espacio libre — descarga el disco del central
/// (donde hoy aterriza <c>volume://</c>). Destinos:
/// <list type="bullet">
///   <item><c>satellite://&lt;vmId&gt;/&lt;sub&gt;</c> — satélite explícito.</item>
///   <item><c>satellite://auto/&lt;sub&gt;</c> — elige el satélite Connected con más disco libre.</item>
/// </list>
/// El blob viaja por el RPC central→satélite (file store). Cap práctico ~60 MiB (límite de mensaje
/// SignalR); para backups grandes usar <c>s3://</c>. La URI final persiste el vmId resuelto (no "auto")
/// para que restore/delete vuelvan al mismo satélite.
/// </summary>
public sealed class SatelliteBackupStorage(
    ISatelliteRpcClient rpc,
    ISatelliteCapacityProvider capacity,
    ILogger<SatelliteBackupStorage> logger) : IBackupStorage
{
    private const long MaxBytes = 60L * 1024 * 1024;

    public bool Supports(string destinationScheme)
        => string.Equals(destinationScheme, "satellite", StringComparison.OrdinalIgnoreCase);

    public async Task<string> WriteAsync(string destinationBase, string fileName, byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.LongLength > MaxBytes)
        {
            throw new InvalidOperationException(
                $"Backup de {content.LongLength} bytes excede el máximo de satellite:// ({MaxBytes} bytes). "
                + "Usa s3:// para backups grandes.");
        }

        var (target, sub) = ParseDestination(destinationBase);
        var vmId = string.Equals(target, "auto", StringComparison.OrdinalIgnoreCase)
            ? await PickSatelliteAsync(content.LongLength, ct).ConfigureAwait(false)
            : target;

        var relativePath = CombineRelative(sub, fileName);
        var resp = await rpc.SendStoreFileAsync(vmId, relativePath, content, ct).ConfigureAwait(false);
        logger.LogInformation(
            "SatelliteBackupStorage: {Bytes} bytes guardados en satélite {VmId} ({Path}).",
            content.Length, vmId, resp.StoredPath);
        return $"satellite://{vmId}/{relativePath}";
    }

    public async Task<byte[]> ReadAsync(string fullPath, CancellationToken ct)
    {
        var (vmId, relativePath) = ParseFull(fullPath);
        return await rpc.SendReadFileAsync(vmId, relativePath, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string fullPath, CancellationToken ct)
    {
        var (vmId, relativePath) = ParseFull(fullPath);
        await rpc.SendDeleteFileAsync(vmId, relativePath, ct).ConfigureAwait(false);
    }

    private async Task<string> PickSatelliteAsync(long needBytes, CancellationToken ct)
    {
        var sats = await capacity.GetSatellitesAsync(ct).ConfigureAwait(false);
        var best = sats
            .Where(s => s.Connected && s.FreeBytes is not null && s.FreeBytes.Value > needBytes)
            .OrderByDescending(s => s.FreeBytes!.Value)
            .FirstOrDefault();
        if (best is null)
        {
            throw new InvalidOperationException(
                "satellite://auto: no hay satélite Connected con disco libre suficiente para el backup.");
        }
        logger.LogInformation(
            "SatelliteBackupStorage: auto eligió {Slug} ({VmId}) con {Free} bytes libres.",
            best.Slug, best.VmId, best.FreeBytes);
        return best.VmId;
    }

    internal static (string Target, string Sub) ParseDestination(string destinationBase)
    {
        var s = StripScheme(destinationBase).Trim('/');
        var slash = s.IndexOf('/');
        if (slash < 0)
        {
            return (s.Length == 0 ? "auto" : s, "backups");
        }
        var target = s[..slash];
        var sub = s[(slash + 1)..].Trim('/');
        return (target.Length == 0 ? "auto" : target, sub.Length == 0 ? "backups" : sub);
    }

    internal static (string VmId, string RelativePath) ParseFull(string fullPath)
    {
        var s = StripScheme(fullPath).Trim('/');
        var slash = s.IndexOf('/');
        if (slash <= 0)
        {
            throw new ArgumentException($"URI satellite:// inválida (sin path): {fullPath}", nameof(fullPath));
        }
        return (s[..slash], s[(slash + 1)..]);
    }

    private static string StripScheme(string uri)
        => uri.StartsWith("satellite://", StringComparison.OrdinalIgnoreCase)
            ? uri["satellite://".Length..]
            : uri;

    internal static string CombineRelative(string sub, string fileName)
        => string.IsNullOrEmpty(sub) ? fileName : $"{sub.Trim('/')}/{fileName}";
}
