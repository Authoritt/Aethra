using Aethra.Modules.Notes.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Notes.Infrastructure.Images;

/// <summary>
/// Implementación local de <see cref="INoteImageStore"/>: cada imagen se guarda en un fichero
/// dentro de <c>Notes:ImageDir</c> con el nombre <c>{imageId}.bin</c>. El nombre original se
/// preserva en la owned entity <see cref="NoteImage.OriginalFilename"/>, no en el filesystem.
///
/// El directorio es configurable. En containers el operador monta un volumen persistente sobre
/// esa ruta (ver <c>deploy/</c>). El path por defecto deriva de
/// <c>AppContext.BaseDirectory/../data/notes</c> para entornos de desarrollo.
/// </summary>
public sealed class LocalNoteImageStore : INoteImageStore
{
    private readonly string _baseDir;
    private readonly ILogger<LocalNoteImageStore> _logger;

    public LocalNoteImageStore(IConfiguration configuration, ILogger<LocalNoteImageStore> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger;
        _baseDir = ResolveBaseDir(configuration);
        Directory.CreateDirectory(_baseDir);
        _logger.LogInformation("LocalNoteImageStore inicializado en {Dir}", _baseDir);
    }

    public async Task<string> SaveAsync(Guid imageId, string originalFilename, string contentType, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var storedFilename = $"{imageId:N}.bin";
        var fullPath = Path.Combine(_baseDir, storedFilename);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct).ConfigureAwait(false);
        await fs.FlushAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Imagen guardada {ImageId} original={Original} contentType={ContentType} bytes={Bytes}",
            imageId, originalFilename, contentType, fs.Length);
        return storedFilename;
    }

    public Task<Stream?> OpenReadAsync(Guid imageId, CancellationToken ct)
    {
        var fullPath = Path.Combine(_baseDir, $"{imageId:N}.bin");
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(Guid imageId, CancellationToken ct)
    {
        var fullPath = Path.Combine(_baseDir, $"{imageId:N}.bin");
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Imagen eliminada {ImageId}", imageId);
        }
        return Task.CompletedTask;
    }

    private static string ResolveBaseDir(IConfiguration configuration)
    {
        var configured = configuration["Notes:ImageDir"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }
        // Default seguro para Windows y Linux: <bin>/../data/notes
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "data", "notes"));
    }
}
