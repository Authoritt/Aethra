using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Deployments.Infrastructure.Build;

/// <summary>
/// Resultado de construir el contexto de build: el tar.gz que se envía al satélite + el SHA
/// realmente materializado (puede diferir del solicitado si el checkout cayó al HEAD del branch)
/// + líneas de log para anexar al build.
/// </summary>
public sealed record BuildContextResult(byte[] TarGz, string ResolvedSha, IReadOnlyList<string> Log);

/// <summary>
/// Construye el contexto de build real: clona el repo Git, materializa el commit y empaqueta el
/// árbol (subdirectorio <c>BaseDirectory</c> si aplica) en un tar.gz que el satélite alimenta a
/// <c>docker build</c>. Reemplaza el "clone simulado" de F9.3 (gap 1).
/// </summary>
public interface IBuildContextBuilder
{
    Task<BuildContextResult> BuildAsync(
        string gitRepoUrl,
        string branch,
        string? gitSha,
        string baseDirectory,
        string? accessToken,
        CancellationToken ct);
}

/// <summary>
/// Implementación sobre el <b>git CLI</b> (clone shallow + checkout del SHA). Empaqueta con
/// <see cref="TarWriter"/> + GZip (built-in .NET) excluyendo <c>.git</c>, respetando
/// <c>BaseDirectory</c>.
///
/// Usamos el binario <c>git</c> en lugar de LibGit2Sharp porque su transporte HTTPS nativo es
/// fiable en Linux/ARM (el bundle nativo de LibGit2Sharp suele fallar en HTTPS en esos targets)
/// y soporta shallow clone, que es más rápido para el caso típico. Requiere <c>git</c> en el PATH
/// del proceso central (la imagen Docker del central lo instala).
/// </summary>
public sealed class BuildContextBuilder(ILogger<BuildContextBuilder> logger) : IBuildContextBuilder
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(5);

    public async Task<BuildContextResult> BuildAsync(
        string gitRepoUrl,
        string branch,
        string? gitSha,
        string baseDirectory,
        string? accessToken,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitRepoUrl);
        var log = new List<string>();
        var workDir = Path.Combine(Path.GetTempPath(), "aethra-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            // === Clone shallow del branch ===
            // El log usa la URL LIMPIA; la URL con token (si el repo es privado) solo viaja en los
            // args de git. El token queda en .git/config del workDir temporal, que se excluye del
            // tar (PackContext) y se borra al final.
            log.Add($"Clonando {gitRepoUrl} (branch={branch})...");
            var cloneUrl = ApplyAccessToken(gitRepoUrl, accessToken);
            var hasBranch = !string.IsNullOrWhiteSpace(branch);
            string[] cloneArgs = hasBranch
                ? ["clone", "--depth", "1", "--branch", branch, "--single-branch", cloneUrl, workDir]
                : ["clone", "--depth", "1", cloneUrl, workDir];

            var clone = await RunGitAsync(cloneArgs, workingDir: null, ct).ConfigureAwait(false);
            if (clone.ExitCode != 0)
            {
                // Reintento sin --branch: el ref puede no ser un branch (tag/sha) o el default
                // difiere del pedido. Clonamos el default y luego intentamos el checkout.
                Directory.Delete(workDir, recursive: true);
                Directory.CreateDirectory(workDir);
                var retry = await RunGitAsync(["clone", "--depth", "1", cloneUrl, workDir], null, ct)
                    .ConfigureAwait(false);
                if (retry.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"git clone falló: {Redact(Trim(retry.StdErr.Length > 0 ? retry.StdErr : clone.StdErr), accessToken)}");
                }
            }

            // === Checkout del SHA solicitado — EXIGIDO, no best-effort ===
            //
            // Antes esto era best-effort: si el fetch/checkout fallaba se anotaba en el log
            // "se usa HEAD del branch" y se seguía empaquetando ESE árbol. Consecuencia: una build
            // identificada como el commit A podía contener el código del commit B. Eso rompe lo que
            // el resto del sistema da por cierto — rollbacks, auditoría y procedencia de artefactos
            // se apoyan en que "build de X" signifique X — y un operador podía promover un artefacto
            // creyendo estar promoviendo el commit que aprobó.
            //
            // Pedir un commit concreto y recibir otro no es una degradación aceptable: es la
            // respuesta equivocada. Si no se puede materializar, la build falla.
            if (!string.IsNullOrWhiteSpace(gitSha))
            {
                var head = await RunGitAsync(["rev-parse", "HEAD"], workDir, ct).ConfigureAwait(false);
                var currentSha = head.StdOut.Trim();
                if (!string.Equals(currentSha, gitSha, StringComparison.OrdinalIgnoreCase))
                {
                    // El clone shallow puede no contener el commit: lo traemos puntualmente.
                    var fetch = await RunGitAsync(["fetch", "--depth", "1", "origin", gitSha!], workDir, ct)
                        .ConfigureAwait(false);
                    var checkout = await RunGitAsync(["checkout", "--detach", gitSha!], workDir, ct)
                        .ConfigureAwait(false);
                    if (checkout.ExitCode != 0)
                    {
                        var detail = Trim(checkout.StdErr.Length > 0 ? checkout.StdErr : fetch.StdErr);
                        throw new InvalidOperationException(
                            $"No se pudo hacer checkout del commit {Short(gitSha!)} solicitado en el branch "
                            + $"{branch}: {Redact(detail, accessToken)}. La build se aborta: empaquetar el HEAD "
                            + "del branch produciría un artefacto con código distinto del commit pedido.");
                    }
                    log.Add($"Checkout commit {Short(gitSha!)} OK.");
                }
            }

            var resolved = await RunGitAsync(["rev-parse", "HEAD"], workDir, ct).ConfigureAwait(false);
            var resolvedSha = resolved.StdOut.Trim();

            // Cinturón y tirantes: que el checkout devuelva 0 no garantiza que HEAD sea lo pedido.
            // Lo que se empaqueta tiene que ser el commit solicitado.
            if (!ResolvedShaSatisfiesRequest(gitSha, resolvedSha))
            {
                throw new InvalidOperationException(
                    $"Se solicitó el commit {Short(gitSha!)} pero el árbol quedó en {Short(resolvedSha)}. "
                    + "La build se aborta para no producir un artefacto que declare un commit que no contiene.");
            }

            // === Resolver raíz del contexto (BaseDirectory) ===
            var contextRoot = workDir;
            if (!string.IsNullOrWhiteSpace(baseDirectory) && baseDirectory.Trim() is { Length: > 0 } sub)
            {
                var normalized = sub.Replace('\\', '/').Trim('/');
                var candidate = Path.GetFullPath(Path.Combine(workDir, normalized));
                if (!candidate.StartsWith(Path.GetFullPath(workDir), StringComparison.Ordinal)
                    || !Directory.Exists(candidate))
                {
                    throw new InvalidOperationException(
                        $"BaseDirectory '{baseDirectory}' no existe en el repo o escapa del contexto.");
                }
                contextRoot = candidate;
            }

            // === Empaquetar tar.gz (excluyendo .git) ===
            var tarGz = await Task.Run(() => PackContext(contextRoot), ct).ConfigureAwait(false);
            log.Add($"Contexto empaquetado: {tarGz.Length / 1024} KiB.");

            return new BuildContextResult(tarGz, resolvedSha, log);
        }
        finally
        {
            TryDelete(workDir);
        }
    }

    private static byte[] PackContext(string contextRoot)
    {
        var rootFull = Path.GetFullPath(contextRoot);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(rootFull, file).Replace('\\', '/');
                // Excluir metadata de git.
                if (rel.StartsWith(".git/", StringComparison.Ordinal) || rel == ".git")
                {
                    continue;
                }
                tar.WriteEntry(file, rel);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Ejecuta <c>git</c> con los argumentos dados y captura stdout/stderr.</summary>
    private async Task<(int ExitCode, string StdOut, string StdErr)> RunGitAsync(
        string[] args, string? workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        // Evita prompts interactivos de credenciales que colgarían el proceso.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "No se pudo ejecutar 'git'. ¿Está instalado en el PATH del central?", ex);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(GitTimeout);
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(proc);
            throw new TimeoutException($"git {args[0]} excedió {GitTimeout.TotalMinutes:0} min.");
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        return (proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void TryKill(Process proc)
    {
        try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Inyecta un token de acceso en una URL HTTPS para clonar repos privados:
    /// <c>https://host/owner/repo</c> → <c>https://x-access-token:&lt;token&gt;@host/owner/repo</c>.
    /// No toca URLs SSH ni URLs que ya traen credenciales (<c>user@host</c>). El token NUNCA se
    /// loguea: solo viaja en los args de <c>git</c> y en <c>.git/config</c> del workDir temporal.
    /// </summary>
    internal static string ApplyAccessToken(string repoUrl, string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)
            || !repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return repoUrl;
        }
        var rest = repoUrl["https://".Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        var authority = slash >= 0 ? rest[..slash] : rest;
        if (authority.Contains('@', StringComparison.Ordinal))
        {
            return repoUrl; // ya trae credenciales — no duplicar
        }
        return $"https://x-access-token:{Uri.EscapeDataString(accessToken)}@{rest}";
    }

    /// <summary>
    /// Defensa en profundidad: elimina el token (crudo y url-encoded) de un texto antes de
    /// loguearlo o propagarlo en una excepción, por si <c>git</c> lo eco en stderr.
    /// </summary>
    internal static string Redact(string text, string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return text;
        }
        return text
            .Replace(accessToken, "***", StringComparison.Ordinal)
            .Replace(Uri.EscapeDataString(accessToken), "***", StringComparison.Ordinal);
    }

    private static string Short(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    /// <summary>
    /// ¿El árbol que se va a empaquetar corresponde al commit que pidió el llamante?
    ///
    /// <para>Sin commit pedido no hay nada que satisfacer: se construye lo que haya en el branch, que
    /// es el contrato normal de un deploy por rama.</para>
    ///
    /// <para><b>El SHA pedido puede venir abreviado.</b> <c>GitSha.Create</c> acepta hex de 7 a 40
    /// caracteres y el validador de build también, mientras que <c>git rev-parse HEAD</c> devuelve
    /// SIEMPRE los 40. Exigir igualdad exacta rechazaría toda build lanzada con un SHA corto —un
    /// caso de uso soportado— y la abortaría sin motivo. Por eso se compara por prefijo.</para>
    ///
    /// <para>El prefijo es identidad suficiente <b>aquí</b>, y no por comodidad: cuando el nombre
    /// abreviado es ambiguo, <c>git checkout</c> falla con "ambiguous argument" y esta función ni
    /// llega a ejecutarse. O sea que la desambiguación ya la hizo git contra el repo real; lo que
    /// queda por descartar es que el árbol acabara en un commit <i>distinto</i> del resuelto, que es
    /// lo que un prefijo no coincidente delata.</para>
    ///
    /// <para>Se ignora la caja porque git emite los SHA en minúsculas pero el llamante puede
    /// haberlos escrito en mayúsculas: el mismo commit escrito distinto sigue siendo el mismo.</para>
    /// </summary>
    internal static bool ResolvedShaSatisfiesRequest(string? requestedSha, string? resolvedSha)
    {
        if (string.IsNullOrWhiteSpace(requestedSha))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(resolvedSha))
        {
            return false;
        }
        return resolvedSha.Trim().StartsWith(requestedSha.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string s) => s.Length > 500 ? s[..500] : s.Trim();

    private void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                // .git deja archivos read-only en Windows; normalizar atributos antes de borrar.
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                }
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo limpiar el contexto de build temporal {Dir}", dir);
        }
    }
}
