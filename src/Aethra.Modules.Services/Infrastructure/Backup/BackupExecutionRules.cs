using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Aethra.Modules.Services.Infrastructure.Backup;

/// <summary>
/// OT-008 T10 — VM en la que corre el proceso central de Aethra. El engine de Postgres solo puede abrir
/// Npgsql directo contra contenedores de ESA VM (misma red Docker); para cualquier otra VM hay que hacer
/// el dump dentro del contenedor por la RPC de exec del satélite. <c>null</c> = desconocida.
/// </summary>
public interface ICentralVmIdProvider
{
    string? GetCentralVmId();
}

/// <summary>
/// Lee <c>Services:CentralVmId</c> (variable de entorno <c>Services__CentralVmId</c>). Sin valor devuelve
/// <c>null</c> y TODO servicio se respalda por el camino local de siempre: sin configurar, el
/// comportamiento es el de antes de T10. El host puede registrar otra implementación (p.ej. la VM del
/// túnel CentralProxy) antes de <c>AddServicesModule</c>.
/// </summary>
public sealed class ConfigurationCentralVmIdProvider(IConfiguration configuration) : ICentralVmIdProvider
{
    public const string ConfigKey = "Services:CentralVmId";

    public string? GetCentralVmId()
    {
        var value = configuration[ConfigKey];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

/// <summary>
/// Reglas puras (sin I/O) del backup remoto de Postgres: decisión local/remoto, límites de tamaño,
/// construcción de comandos shell y decodificación de la salida.
/// </summary>
public static class BackupExecutionRules
{
    /// <summary>Cabecera (primera línea del SQL) que distingue el formato remoto del COPY-v1 local.</summary>
    public const string RemoteDumpHeader = "-- aethra-pg-dump remote v1";

    /// <summary>
    /// Tope del dump comprimido. El stdout viaja en base64 (x4/3) en UN mensaje SignalR de 64 MiB
    /// (<c>apps/api/Program.cs</c>, MaximumReceiveMessageSize) y <c>satellite://</c> admite 60 MB:
    /// 40 MiB → ~53,4 MiB en base64, con holgura para el sobre JSON. Superarlo es fallo explícito.
    /// </summary>
    public const long MaxCompressedDumpBytes = 40L * 1024 * 1024;

    /// <summary>Trozo de base64 por exec en el restore (un arg de <c>sh -c</c> admite ≤128 KiB en Linux).</summary>
    public const int RestoreChunkChars = 96_000;

    public const string TooLargeMarker = "aethra-dump-too-large";

    /// <summary>
    /// Remoto solo si el central conoce su propia VM Y el servicio vive en otra. Ante la duda (VM del
    /// central desconocida o TargetVmId vacío) → local: es el comportamiento previo a T10.
    /// </summary>
    public static bool RunsOnRemoteSatellite(string? serviceTargetVmId, string? centralVmId)
    {
        if (string.IsNullOrWhiteSpace(serviceTargetVmId) || string.IsNullOrWhiteSpace(centralVmId))
        {
            return false;
        }
        return !string.Equals(serviceTargetVmId.Trim(), centralVmId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    public static string BuildDumpCommand(string tag, string username, string password, string database)
        => "set -e; f=/tmp/aethra-bk-" + tag + "; trap 'rm -f \"$f.sql\" \"$f.gz\"' EXIT; "
           + "export PGPASSWORD=" + ShellQuote(password) + "; "
           + "{ echo " + ShellQuote(RemoteDumpHeader) + "; "
           + "pg_dump -U " + ShellQuote(username) + " -d " + ShellQuote(database)
           + " --clean --if-exists --no-owner --no-privileges; } > \"$f.sql\"; "
           + "gzip -c \"$f.sql\" > \"$f.gz\"; "
           + "sz=$(wc -c < \"$f.gz\"); "
           + "if [ \"$sz\" -gt " + MaxCompressedDumpBytes + " ]; then echo \"" + TooLargeMarker + ":$sz\" >&2; exit 3; fi; "
           + "base64 \"$f.gz\" | tr -d '\\n'";

    public static string BuildRestoreAppendCommand(string tag, string base64Chunk, bool first)
        => "f=/tmp/aethra-rs-" + tag + "; printf %s " + ShellQuote(base64Chunk)
           + (first ? " > " : " >> ") + "\"$f.b64\"";

    public static string BuildRestoreApplyCommand(string tag, string username, string password, string database)
        => "set -e; f=/tmp/aethra-rs-" + tag + "; trap 'rm -f \"$f.b64\" \"$f.sql\"' EXIT; "
           + "base64 -d \"$f.b64\" | gunzip -c > \"$f.sql\"; "
           + "export PGPASSWORD=" + ShellQuote(password) + "; "
           + "psql -U " + ShellQuote(username) + " -d " + ShellQuote(database)
           + " -v ON_ERROR_STOP=1 --single-transaction -q -f \"$f.sql\"";

    public static string BuildRestoreCleanupCommand(string tag)
        => "rm -f /tmp/aethra-rs-" + tag + ".b64 /tmp/aethra-rs-" + tag + ".sql";

    /// <summary>
    /// Convierte el stdout del dump remoto (base64 de un gzip) en los bytes del backup. Lanza
    /// <see cref="InvalidOperationException"/> con motivo explícito ante cualquier anomalía; nunca trunca.
    /// </summary>
    public static byte[] DecodeDumpOutput(int exitCode, bool timedOut, string stdout, string stderr)
    {
        if (timedOut)
        {
            throw new InvalidOperationException("pg_dump remoto: el exec en el satélite excedió el timeout.");
        }
        if (exitCode == 3 && (stderr ?? string.Empty).Contains(TooLargeMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"pg_dump remoto: el dump comprimido supera {MaxCompressedDumpBytes} bytes (tope por el mensaje "
                + "SignalR de 64 MiB y satellite:// de 60 MB). Backup NO creado; no se trunca.");
        }
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"pg_dump remoto falló (exit {exitCode}): {Truncate(stderr, 500)}");
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String((stdout ?? string.Empty).Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("pg_dump remoto: la salida no es base64 válido.", ex);
        }
        if (bytes.Length == 0 || bytes.Length > MaxCompressedDumpBytes)
        {
            throw new InvalidOperationException(
                $"pg_dump remoto: tamaño inválido ({bytes.Length} bytes; máx {MaxCompressedDumpBytes}).");
        }
        if (bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b)
        {
            throw new InvalidOperationException("pg_dump remoto: la salida no es un gzip.");
        }
        return bytes;
    }

    /// <summary>Verifica que un backup sea del formato remoto y de tamaño restaurable.</summary>
    public static void EnsureRestorableRemote(byte[] backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        if (backup.Length > MaxCompressedDumpBytes)
        {
            throw new InvalidOperationException(
                $"Restore remoto: el backup ({backup.Length} bytes) supera el tope de {MaxCompressedDumpBytes}.");
        }
        string? first;
        try
        {
            using var ms = new MemoryStream(backup);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, new UTF8Encoding(false));
            first = reader.ReadLine();
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException("Restore remoto: el backup no es un gzip válido.", ex);
        }
        if (!string.Equals(first, RemoteDumpHeader, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Restore remoto: el backup no es del formato 'aethra-pg-dump remote v1' "
                + "(¿es un backup local COPY-v1 de otro servicio?). No se aplica.");
        }
    }

    public static IEnumerable<string> SplitBase64(string base64)
    {
        for (var i = 0; i < base64.Length; i += RestoreChunkChars)
        {
            yield return base64.Substring(i, Math.Min(RestoreChunkChars, base64.Length - i));
        }
    }

    private static string Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : s.Length <= max ? s : s[..max] + "...";
}
