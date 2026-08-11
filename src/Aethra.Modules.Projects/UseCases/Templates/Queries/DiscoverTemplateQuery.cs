using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Aethra.Modules.Projects.UseCases.Templates.Dtos;
using Aethra.Shared.Infrastructure.Cqrs;
using Aethra.Shared.Infrastructure.Http;
using Aethra.Shared.Kernel.Errors;
using Aethra.Shared.Kernel.Results;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Aethra.Modules.Projects.UseCases.Templates.Queries;

/// <summary>
/// F11.2 — Inspecciona un repo Git (shallow clone) y devuelve qué estrategia de build se puede usar:
/// Dockerfile, DockerCompose, Nixpacks. También sugiere puertos expuestos para prellenar la UI.
/// No persiste nada: solo lee la raíz del repo y devuelve el diagnóstico al cliente.
/// </summary>
/// <param name="GitRepoUrl">URL del repo a inspeccionar (HTTPS o SSH).</param>
/// <param name="Branch">Branch a clonar; null/empty para usar el default del repo.</param>
public sealed record DiscoverTemplateQuery(string GitRepoUrl, string? Branch)
    : IQuery<TemplateDiscoverResult>;

public sealed partial class DiscoverTemplateValidator : AbstractValidator<DiscoverTemplateQuery>
{
    public DiscoverTemplateValidator()
    {
        RuleFor(c => c.GitRepoUrl)
            .NotEmpty()
            .MaximumLength(500)
            .Must(IsLikelyGitUrl)
            .WithMessage("GitRepoUrl no parece una URL válida (debe empezar con https://, git@, ssh:// o git://).");
        RuleFor(c => c.Branch).MaximumLength(255);
    }

    private static bool IsLikelyGitUrl(string url)
        => !string.IsNullOrWhiteSpace(url)
            && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("git://", StringComparison.OrdinalIgnoreCase));
}

internal sealed partial class DiscoverTemplateHandler(
    ILogger<DiscoverTemplateHandler> logger,
    IOutboundDestinationGuard destinations)
    : IQueryHandler<DiscoverTemplateQuery, TemplateDiscoverResult>
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    public async Task<Result<TemplateDiscoverResult>> Handle(
        DiscoverTemplateQuery request,
        CancellationToken cancellationToken)
    {
        // El repo lo elige quien llama y el clone se ejecuta DESDE el plano de control, que alcanza
        // loopback, la malla privada y el endpoint de metadatos de la nube. Sin esta comprobacion,
        // discovery es un SSRF con git de transporte.
        //
        // Limitacion conocida y aceptada: git resuelve el nombre por su cuenta al conectar, asi que
        // aqui solo se puede validar por adelantado. El rebinding entre esta comprobacion y el clone
        // queda fuera de alcance sin un proxy de salida; para los destinos HTTP del propio proceso si
        // se cierra, porque la validacion ocurre en el socket (GuardOutboundDestinations).
        var destinationHost = ExtractHost(request.GitRepoUrl);
        if (destinationHost is null)
        {
            return Error.Validation("template.discover_invalid_url",
                "No se pudo determinar el host del repositorio.");
        }
        var verdict = await destinations.CheckHostAsync(destinationHost, cancellationToken).ConfigureAwait(false);
        if (!verdict.Allowed)
        {
            return Error.Validation("template.destination_blocked", verdict.Reason!);
        }

        var workDir = Path.Combine(Path.GetTempPath(), "aethra-discover", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            // Shallow clone. Si el llamante pidió una rama CONCRETA y no se puede clonar, es un
            // error suyo (typo, rama borrada, sin permisos) y así se le devuelve.
            //
            // Antes había un reintento sin `--branch` que clonaba la rama POR DEFECTO y seguía
            // adelante en silencio. El resultado era una discovery exitosa que describía código de
            // otra rama: un typo producía una inspección plausible del sitio equivocado, y a partir
            // de ahí se podía crear o desplegar un template creyendo haber analizado lo pedido.
            // Sustituir la entrada del usuario por otra sin avisar convierte un error detectable en
            // uno silencioso.
            var hasBranch = !string.IsNullOrWhiteSpace(request.Branch);
            string[] cloneArgs = hasBranch
                ? ["clone", "--depth", "1", "--branch", request.Branch!.Trim(), "--single-branch", request.GitRepoUrl, workDir]
                : ["clone", "--depth", "1", request.GitRepoUrl, workDir];

            var clone = await RunGitAsync(cloneArgs, cancellationToken).ConfigureAwait(false);
            if (clone.ExitCode != 0)
            {
                return hasBranch
                    ? Error.Validation("template.branch_not_found",
                        $"No se pudo clonar la rama '{request.Branch!.Trim()}' del repo: {Trim(clone.StdErr)}. "
                        + "Comprueba que existe y que la credencial tiene acceso. No se inspecciona otra rama "
                        + "en su lugar: el resultado describiría código distinto del solicitado.")
                    : Error.Validation("template.discover_clone_failed",
                        $"No se pudo clonar el repo: {Trim(clone.StdErr)}");
            }

            // === Inspección de la raíz ===
            var hasDockerfile = File.Exists(Path.Combine(workDir, "Dockerfile"));
            var hasCompose = File.Exists(Path.Combine(workDir, "docker-compose.yml"))
                || File.Exists(Path.Combine(workDir, "compose.yml"))
                || File.Exists(Path.Combine(workDir, "docker-compose.yaml"))
                || File.Exists(Path.Combine(workDir, "compose.yaml"));
            var hasNixpacksToml = File.Exists(Path.Combine(workDir, "nixpacks.toml"));

            var detectedLanguages = DetectLanguages(workDir);

            string suggestedBuildType;
            if (hasDockerfile)
            {
                suggestedBuildType = "Dockerfile";
            }
            else if (hasCompose)
            {
                suggestedBuildType = "DockerCompose";
            }
            else if (hasNixpacksToml || detectedLanguages.Count > 0)
            {
                suggestedBuildType = "Nixpacks";
            }
            else
            {
                // Sin pistas: el operador deberá decidir, pero mostramos Dockerfile como fallback
                // porque es el modo histórico — la UI deja al usuario sobreescribir.
                suggestedBuildType = "Dockerfile";
            }

            // Puertos: si hay Dockerfile, intentamos parsear EXPOSE.
            // Sino, default por lenguaje (node=3000, python=5000, go=8080, ruby=3000, php=80).
            IReadOnlyList<int> exposedPorts = hasDockerfile
                ? ParseExposeFromDockerfile(Path.Combine(workDir, "Dockerfile"))
                : DefaultPortsForLanguages(detectedLanguages);

            logger.LogInformation(
                "Discover: repo={Repo} branch={Branch} → buildType={Type}, langs=[{Langs}], ports=[{Ports}]",
                request.GitRepoUrl,
                request.Branch ?? "(default)",
                suggestedBuildType,
                string.Join(',', detectedLanguages),
                string.Join(',', exposedPorts));

            return new TemplateDiscoverResult(
                detectedLanguages: detectedLanguages,
                hasDockerfile: hasDockerfile,
                hasCompose: hasCompose,
                hasNixpacksToml: hasNixpacksToml,
                suggestedBuildType: suggestedBuildType,
                exposedPorts: exposedPorts);
        }
        catch (TimeoutException ex)
        {
            return Error.Validation("template.discover_timeout", ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discover falló para repo {Repo}", request.GitRepoUrl);
            return Error.Validation("template.discover_failed", ex.Message);
        }
        finally
        {
            TryDelete(workDir);
        }
    }

    // -------------------------------------------------------------------------
    // Detección heurística de lenguajes a partir de archivos en la raíz.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Host de una URL de repositorio, en cualquiera de las formas que acepta el validador:
    /// <c>https://host/...</c>, <c>ssh://host/...</c>, <c>git://host/...</c> y el SCP
    /// <c>git@host:ruta</c>, que NO es una URI valida y hay que partir a mano.
    /// Devuelve <c>null</c> si no se puede determinar: sin host no se puede autorizar el destino.
    /// </summary>
    internal static string? ExtractHost(string? repoUrl)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            return null;
        }
        var value = repoUrl.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            return uri.IdnHost;
        }

        // Forma SCP: [user@]host:ruta. Se corta por el PRIMER ':' que va despues del '@' (si lo hay)
        // para no confundirse con el ':' de un puerto ni con el de la ruta.
        var atIndex = value.IndexOf('@');
        var hostStart = atIndex >= 0 ? atIndex + 1 : 0;
        var colonIndex = value.IndexOf(':', hostStart);
        if (colonIndex > hostStart)
        {
            var host = value[hostStart..colonIndex];
            return string.IsNullOrWhiteSpace(host) ? null : host;
        }
        return null;
    }

    private static List<string> DetectLanguages(string repoRoot)
    {
        var langs = new List<string>();
        if (File.Exists(Path.Combine(repoRoot, "package.json"))) { langs.Add("node"); }
        if (File.Exists(Path.Combine(repoRoot, "requirements.txt"))
            || File.Exists(Path.Combine(repoRoot, "pyproject.toml"))
            || File.Exists(Path.Combine(repoRoot, "Pipfile"))) { langs.Add("python"); }
        if (File.Exists(Path.Combine(repoRoot, "go.mod"))) { langs.Add("go"); }
        if (File.Exists(Path.Combine(repoRoot, "Cargo.toml"))) { langs.Add("rust"); }
        if (File.Exists(Path.Combine(repoRoot, "Gemfile"))) { langs.Add("ruby"); }
        if (File.Exists(Path.Combine(repoRoot, "composer.json"))) { langs.Add("php"); }
        if (File.Exists(Path.Combine(repoRoot, "pom.xml"))
            || File.Exists(Path.Combine(repoRoot, "build.gradle"))
            || File.Exists(Path.Combine(repoRoot, "build.gradle.kts"))) { langs.Add("java"); }
        if (File.Exists(Path.Combine(repoRoot, "mix.exs"))) { langs.Add("elixir"); }
        if (File.Exists(Path.Combine(repoRoot, "deno.json"))
            || File.Exists(Path.Combine(repoRoot, "deno.jsonc"))) { langs.Add("deno"); }
        return langs;
    }

    private static IReadOnlyList<int> DefaultPortsForLanguages(List<string> langs)
    {
        if (langs.Count == 0)
        {
            return [];
        }
        var first = langs[0];
        return first switch
        {
            "node" => [3000],
            "python" => [5000],
            "go" => [8080],
            "rust" => [8080],
            "ruby" => [3000],
            "php" => [80],
            "java" => [8080],
            "elixir" => [4000],
            "deno" => [8000],
            _ => [],
        };
    }

    [GeneratedRegex(@"^\s*EXPOSE\s+(.+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ExposeRegex();

    private static List<int> ParseExposeFromDockerfile(string dockerfilePath)
    {
        try
        {
            var text = File.ReadAllText(dockerfilePath);
            var ports = new List<int>();
            foreach (Match m in ExposeRegex().Matches(text))
            {
                // EXPOSE puede venir como "EXPOSE 80 443" o "EXPOSE 8080/tcp".
                foreach (var token in m.Groups[1].Value.Split(
                    [' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var slash = token.IndexOf('/', StringComparison.Ordinal);
                    var num = slash > 0 ? token[..slash] : token;
                    if (int.TryParse(num, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var p)
                        && p > 0 && p <= 65535)
                    {
                        ports.Add(p);
                    }
                }
            }
            return ports;
        }
        catch
        {
            return [];
        }
    }

    // -------------------------------------------------------------------------
    // Wrappers de git CLI (espeja al BuildContextBuilder, simplificado).
    // -------------------------------------------------------------------------

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunGitAsync(
        string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) { psi.ArgumentList.Add(a); }
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

    private static string Trim(string s) => s.Length > 500 ? s[..500] : s.Trim();

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                // .git deja archivos read-only en Windows; normalizar atributos antes de borrar.
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* best-effort */ }
                }
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException) { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }
}
