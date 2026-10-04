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
    /// <summary>
    /// Cabecera (primera línea del script) del formato remoto. v2 (T13) = TODAS las bases no-plantilla + roles;
    /// la distingue del COPY-v1 local y del remoto v1 (T11, una sola base) que ya NO se restaura.
    /// </summary>
    public const string RemoteDumpHeader = "-- aethra-pg-dump remote v2";

    /// <summary>Cabecera del formato remoto v1 (T11: solo la base del admin). Se rechaza de forma explícita.</summary>
    public const string RemoteDumpHeaderV1 = "-- aethra-pg-dump remote v1";

    /// <summary>
    /// Última línea de un dump remoto COMPLETO. Solo se emite si TODAS las etapas (roles + cada pg_dump) acabaron
    /// bien: un gzip válido pero cortado (subshell abortado, base64 truncado) no la lleva y se rechaza.
    /// </summary>
    public const string RemoteDumpEnd = "-- aethra-pg-dump end";

    /// <summary>
    /// Base temporal en la que se carga cada base antes de intercambiarla por la viva. Si una carga falla, la
    /// base viva sigue intacta. Se excluye del dump (resto de una ejecución interrumpida).
    /// </summary>
    public const string RestoreTempDatabase = "aethra_restore_tmp";

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

    /// <summary>
    /// Fallo de una etapa del pipeline del dump (pg_dump). El <c>sh</c> de la imagen puede ser dash (sin
    /// <c>pipefail</c>) o busybox, así que el estado de pg_dump se registra en un fichero marcador en vez de
    /// depender del código de salida de la tubería.
    /// </summary>
    public const string DumpFailedMarker = "aethra-dump-failed";

    /// <summary>Activa pipefail solo si el sh lo soporta (dash no); el resto de la lógica no depende de él.</summary>
    private const string TryPipefail = "(set -o pipefail) 2>/dev/null && set -o pipefail; ";

    // SEGURIDAD (G2 B1): NINGÚN comando lleva la contraseña (ni PGPASSWORD ni -W): el argv de `sh -c` es visible
    // en `ps` del contenedor y del host y en `docker events`. Se conecta SIN -h, por el socket unix local, donde
    // la imagen oficial `postgres` usa `local all all trust` (las plantillas no tocan pg_hba). `-w` impide el
    // prompt: si un pg_hba exigiera contraseña en local, pg_dump/psql FALLAN explícitamente en vez de colgarse.
    //
    // T13 — formato remoto v2: UN gzip con un script psql que restaura TODAS las bases no-plantilla + roles.
    //
    //   -- aethra-pg-dump remote v2
    //   -- aethra-section globals            BEGIN; <pg_dumpall --roles-only, CREATE ROLE idempotente>; COMMIT;
    //   -- aethra-section database <db>      (una por base)
    //       DROP DATABASE IF EXISTS aethra_restore_tmp WITH (FORCE);
    //       CREATE DATABASE aethra_restore_tmp ... OWNER <dueño>;     (fuera de transaccion: no puede ir en una)
    //       \connect aethra_restore_tmp
    //       BEGIN; <pg_dump de la base>; COMMIT;                      (la carga de la base es atomica)
    //       \connect template1
    //       DROP DATABASE IF EXISTS "<db>" WITH (FORCE);              (intercambio: milisegundos sin la base viva)
    //       ALTER DATABASE aethra_restore_tmp RENAME TO "<db>";
    //   -- aethra-pg-dump end
    //
    // Por que pg_dumpall --roles-only + un pg_dump por base y NO un pg_dumpall entero: pg_dumpall emite
    // DROP/CREATE DATABASE + \connect y luego los objetos sin transaccion posible por base (DROP DATABASE no admite
    // BEGIN), asi que una carga que falla a medias deja la base viva vacia o parcial. Con la carga en una base
    // temporal + intercambio, una carga fallida deja la base viva INTACTA. Ademas pg_dumpall volcaria tablespaces y
    // con --clean exigiria DROP ROLE (imposible sobre el rol en uso o dueño de objetos de otras bases).
    //
    // ATOMICIDAD: globals = 1 transaccion; cada base = 1 transaccion de carga + intercambio (DROP+RENAME, dos
    // sentencias seguidas); ENTRE bases no hay atomicidad: el restore se detiene en la primera que falla
    // (ON_ERROR_STOP) -> las anteriores quedan restauradas; la fallida y las siguientes conservan su estado vivo.
    // Cada pg_dump es una instantanea propia (sin snapshot comun entre bases). NO cubierto: ALTER DATABASE ... SET,
    // tablespaces, locale ICU (se asume libc), nombres de base fuera de [A-Za-z0-9_.-] (el dump falla explicito).
    private const string ListableDatabases =
        "FROM pg_database WHERE NOT datistemplate AND datallowconn AND datname <> $q$" + RestoreTempDatabase + "$q$";

    // Todo el SQL usa dollar-quoting ($q$..$q$): NINGUNA comilla simple, asi cabe en un argumento de 'sh -c'.
    private const string ListDatabasesSql = "SELECT datname " + ListableDatabases + " ORDER BY datname";

    private const string CountBadNamesSql =
        "SELECT count(*) " + ListableDatabases + " AND (datname ~ $q$[^A-Za-z0-9_.-]$q$ OR datname ~ $q$^[.-]$q$)";

    // El nombre de la base (ya validado) se concatena en el shell:  '<prefijo>'"$db"'$q$'
    private const string CreateTempDatabaseSqlPrefix =
        "SELECT format($q$CREATE DATABASE " + RestoreTempDatabase + " WITH TEMPLATE template0 OWNER %I ENCODING %L "
        + "LC_COLLATE %L LC_CTYPE %L;$q$, pg_get_userbyid(datdba), pg_encoding_to_char(encoding), datcollate, datctype) "
        + "FROM pg_database WHERE datname = $q$";

    // pg_dumpall emite `CREATE ROLE x;` a secas (falla si ya existe, p.ej. el propio postgres): se vuelve idempotente.
    private const string RolesSedScript =
        "s/^CREATE ROLE \\(.*\\);$/DO $q$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE quote_ident(rolname) = $n$\\1$n$) "
        + "THEN CREATE ROLE \\1; END IF; END $q$;/";

    // busybox ash: sin pipefail fiable, y `echo` interpreta barras (\c) -> printf '%s\n'. El estado de CADA etapa
    // (psql, pg_dumpall, cada pg_dump) va al fichero $f.rc; `set +e` dentro del grupo para que una etapa caida no
    // aborte el subshell dejando un gzip valido pero cortado (ademas lo caza la linea RemoteDumpEnd).
    public static string BuildDumpCommand(string tag, string username)
    {
        var u = ShellQuote(username);
        const string fail = " || echo 1 > \"$f.rc\"";
        var psql = "psql -w -U " + u + " -d template1 -X -At";
        return "set -e; " + TryPipefail + "f=/tmp/aethra-bk-" + tag + "; trap 'rm -f \"$f.gz\" \"$f.rc\"' EXIT; rm -f \"$f.rc\"; "
           + "{ set +e; "
           + "printf '%s\\n' " + ShellQuote(RemoteDumpHeader) + " '-- aethra-section globals' 'BEGIN;'; "
           + "{ pg_dumpall -w -U " + u + " --roles-only </dev/null" + fail + "; } | sed " + ShellQuote(RolesSedScript) + "; "
           + "printf '%s\\n' 'COMMIT;'; "
           + "dbs=$(" + psql + " -c " + ShellQuote(ListDatabasesSql) + " </dev/null)" + fail + "; "
           + "bad=$(" + psql + " -c " + ShellQuote(CountBadNamesSql) + " </dev/null)" + fail + "; "
           + "[ \"$bad\" = 0 ] || { echo \"aethra-dump-invalid-db-name\" >&2; echo 1 > \"$f.rc\"; }; "
           + "[ -n \"$dbs\" ] || { echo \"aethra-dump-no-databases\" >&2; echo 1 > \"$f.rc\"; }; "
           + "for db in $dbs; do "
           + "if [ -e \"$f.rc\" ]; then break; fi; "
           + "printf '%s\\n' \"-- aethra-section database $db\" 'DROP DATABASE IF EXISTS " + RestoreTempDatabase + " WITH (FORCE);'; "
           + psql + " -c '" + CreateTempDatabaseSqlPrefix + "'\"$db\"'$q$' </dev/null" + fail + "; "
           + "printf '%s\\n' '\\connect " + RestoreTempDatabase + "' 'BEGIN;'; "
           + "{ pg_dump -w -U " + u + " -d \"$db\" </dev/null" + fail + "; }; "
           + "printf '%s\\n' 'COMMIT;' '\\connect template1' "
           + "\"DROP DATABASE IF EXISTS \\\"$db\\\" WITH (FORCE);\" "
           + "\"ALTER DATABASE " + RestoreTempDatabase + " RENAME TO \\\"$db\\\";\"; "
           + "done; "
           + "if [ ! -e \"$f.rc\" ]; then printf '%s\\n' " + ShellQuote(RemoteDumpEnd) + "; fi; "
           + "} | gzip -c > \"$f.gz\"; "
           + "if [ -e \"$f.rc\" ]; then echo \"" + DumpFailedMarker + "\" >&2; exit 4; fi; "
           + "sz=$(wc -c < \"$f.gz\"); "
           + "if [ \"$sz\" -gt " + MaxCompressedDumpBytes + " ]; then echo \"" + TooLargeMarker + ":$sz\" >&2; exit 3; fi; "
           + "base64 -w 0 \"$f.gz\"";
    }

    public static string BuildRestoreAppendCommand(string tag, string base64Chunk, bool first)
        => "f=/tmp/aethra-rs-" + tag + "; printf %s " + ShellQuote(base64Chunk)
           + (first ? " > " : " >> ") + "\"$f.b64\"";

    // Sin .sql intermedio: primero `gunzip -t` valida el gzip ENTERO (un gzip truncado/corrupto no debe llegar a
    // psql) y luego se canaliza a psql. SIN --single-transaction: el script lleva sus propias transacciones por base
    // (DROP/CREATE DATABASE no caben en una) y ON_ERROR_STOP detiene el restore en el primer error (psql exit 3).
    public static string BuildRestoreApplyCommand(string tag, string username)
        => "set -e; " + TryPipefail + "f=/tmp/aethra-rs-" + tag + "; trap 'rm -f \"$f.b64\"' EXIT; "
           + "base64 -d \"$f.b64\" | gunzip -t; "
           + "base64 -d \"$f.b64\" | gunzip -c | psql -w -U " + ShellQuote(username) + " -d template1 -X"
           + " -v ON_ERROR_STOP=1 -q";

    // Limpieza (mejor esfuerzo): ficheros y base temporal de una carga que falló a medias.
    public static string BuildRestoreCleanupCommand(string tag, string username)
        => "rm -f /tmp/aethra-rs-" + tag + ".b64 /tmp/aethra-rs-" + tag + ".sql; "
           + "psql -w -U " + ShellQuote(username) + " -d template1 -X -q -c "
           + ShellQuote("DROP DATABASE IF EXISTS " + RestoreTempDatabase + " WITH (FORCE)") + " </dev/null || true";

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
        if (exitCode == 4 && (stderr ?? string.Empty).Contains(DumpFailedMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"pg_dump remoto falló (pg_dump terminó con error): {Truncate(stderr, 500)}");
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
        // G2 D3: no basta la cabecera mágica; un base64 truncado pero decodificable pasaría. Se descomprime
        // ENTERO (el trailer CRC/tamaño del gzip lo valida .NET) y se exige la cabecera del formato remoto.
        try
        {
            EnsureRestorableRemote(bytes);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException("pg_dump remoto: salida incompleta o corrupta. " + ex.Message, ex);
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
        bool truncated;
        var tail = new byte[512];
        var tailLen = 0;
        string lastLine;
        try
        {
            using var ms = new MemoryStream(backup);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, new UTF8Encoding(false));
            first = reader.ReadLine();
            // .NET NO lanza ante un gzip truncado (devuelve EOF sin más): se descomprime de nuevo contando bytes y
            // se contrasta con ISIZE (últimos 4 bytes del gzip, tamaño mod 2^32). Un corte casi nunca lo cumple.
            ms.Position = 0;
            using var gz2 = new GZipStream(ms, CompressionMode.Decompress);
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = gz2.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += n;
                // Cola del script (últimos bytes) para exigir RemoteDumpEnd sin materializar el SQL entero.
                if (n >= tail.Length)
                {
                    Array.Copy(buffer, n - tail.Length, tail, 0, tail.Length);
                    tailLen = tail.Length;
                }
                else
                {
                    var keep = Math.Min(tailLen, tail.Length - n);
                    Array.Copy(tail, tailLen - keep, tail, 0, keep);
                    Array.Copy(buffer, 0, tail, keep, n);
                    tailLen = keep + n;
                }
            }
            lastLine = Encoding.UTF8.GetString(tail, 0, tailLen).TrimEnd('\r', '\n');
            lastLine = lastLine[(lastLine.LastIndexOf('\n') + 1)..];
            truncated = backup.Length < 18 || (uint)total != BitConverter.ToUInt32(backup, backup.Length - 4);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            throw new InvalidOperationException("Restore remoto: el backup no es un gzip válido.", ex);
        }
        if (truncated)
        {
            throw new InvalidOperationException("Restore remoto: el backup está truncado o corrupto (tamaño del gzip no coincide).");
        }
        if (string.Equals(first, RemoteDumpHeaderV1, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Restore remoto: el backup es del formato 'aethra-pg-dump remote v1' (T11), que solo cubría UNA base "
                + "y ya no se restaura; haz un backup nuevo (v2). No se aplica.");
        }
        if (!string.Equals(first, RemoteDumpHeader, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Restore remoto: el backup no es del formato 'aethra-pg-dump remote v2' "
                + "(¿es un backup local COPY-v1 de otro servicio?). No se aplica.");
        }
        if (!string.Equals(lastLine, RemoteDumpEnd, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Restore remoto: el backup no termina con '" + RemoteDumpEnd + "' (dump incompleto). No se aplica.");
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
