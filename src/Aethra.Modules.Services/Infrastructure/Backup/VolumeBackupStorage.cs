using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// Storage de backups en disco local. URL <c>volume://service-slug</c> mapea a
/// <c>{rootDir}/{service-slug}/{filename}</c>. Configurable via <c>Services:Backup:VolumeRoot</c>.
/// </summary>
public sealed class VolumeBackupStorage(IConfiguration config, ILogger<VolumeBackupStorage> logger) : IBackupStorage
{
    private readonly string _root = ResolveRoot(config);

    public bool Supports(string destinationScheme)
        => string.Equals(destinationScheme, "volume", StringComparison.OrdinalIgnoreCase);

    public async Task<string> WriteAsync(string destinationBase, string fileName, byte[] content, CancellationToken ct)
    {
        var sub = NormalizeSubPath(destinationBase);
        var dir = Path.Combine(_root, sub);
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, fileName);
        await File.WriteAllBytesAsync(fullPath, content, ct).ConfigureAwait(false);
        logger.LogInformation("VolumeBackupStorage: escribi {Bytes} bytes en {Path}", content.Length, fullPath);
        return ToVolumeUri(sub, fileName);
    }

    public async Task<byte[]> ReadAsync(string fullPath, CancellationToken ct)
    {
        var fsPath = VolumeUriToFsPath(fullPath);
        return await File.ReadAllBytesAsync(fsPath, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(string fullPath, CancellationToken ct)
    {
        var fsPath = VolumeUriToFsPath(fullPath);
        if (File.Exists(fsPath))
        {
            File.Delete(fsPath);
            logger.LogInformation("VolumeBackupStorage: borrado {Path}", fsPath);
        }
        return Task.CompletedTask;
    }

    private static string NormalizeSubPath(string destinationBase)
    {
        // destinationBase shape: "volume://path/to/dir" o "path/to/dir"
        var s = destinationBase;
        if (s.StartsWith("volume://", StringComparison.OrdinalIgnoreCase))
        {
            s = s["volume://".Length..];
        }
        // Sanitizar: solo a-z 0-9 -/_.
        var chars = s.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '/' || c == '.')
            .ToArray();
        return new string(chars).Trim('/');
    }

    private string ToVolumeUri(string sub, string fileName)
        => $"volume://{sub}/{fileName}";

    private string VolumeUriToFsPath(string uri)
    {
        if (!uri.StartsWith("volume://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"URI no es volume://: {uri}", nameof(uri));
        }
        var rel = uri["volume://".Length..];
        return Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ResolveRoot(IConfiguration config)
    {
        var root = config["Services:Backup:VolumeRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = OperatingSystem.IsWindows()
                ? Path.Combine(Path.GetTempPath(), "aethra", "backups")
                : "/var/lib/aethra/backups";
        }
        Directory.CreateDirectory(root);
        return root;
    }
}
