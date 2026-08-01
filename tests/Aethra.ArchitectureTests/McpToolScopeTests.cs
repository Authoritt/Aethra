using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Aethra.ArchitectureTests;

/// <summary>
/// Toda tool MCP comprueba su scope antes de hacer nada.
///
/// <para>
/// Una tool sin guarda es invocable por CUALQUIER API key válida, sin importar los scopes
/// con los que se emitió: la autenticación resuelve el principal y emite un claim por scope
/// (<c>AethraApiKeyAuthHandler</c>), pero nada en el pipeline MCP comprueba ese claim — lo
/// comprueba cada tool en su cuerpo, a mano, con
/// <c>if (!caller.HasScope(...)) return McpResponses.InsufficientScope(...)</c>.
/// </para>
///
/// <para>
/// Hoy la propiedad se cumple en las 117 tools. El problema no es el estado, es cómo se
/// sostiene: por 117 repeticiones correctas que nadie verifica. Nada impide que la tool 118
/// se publique sin guarda, y el fallo sería silencioso — la tool responde 200 y hace su
/// trabajo. Este test convierte esas repeticiones en UNA invariante que corre en CI.
/// </para>
///
/// <para>
/// <b>Lo que este fence NO prueba</b> (escrito aquí en vez de quedar implícito, porque un
/// check verde que promete de más es peor que ninguno): prueba que la guarda EXISTE en el
/// cuerpo del método, no que se ejecute ANTES del efecto. Una tool que mutara y después
/// comprobara el scope pasaría este test. Cubrir eso pide mover la guarda a un filtro del
/// pipeline — que es la corrección estructural real, y es un refactor de las 117.
/// </para>
///
/// <para>
/// Es un fence sobre el FUENTE, no por reflection, a propósito: la guarda vive en el cuerpo
/// del método y el cuerpo no es observable por reflection sin decodificar IL.
/// </para>
/// </summary>
public sealed class McpToolScopeTests
{
    private const string ToolAttribute = "[McpServerTool(";

    [Fact]
    public void Toda_tool_mcp_comprueba_su_scope()
    {
        var archivos = ArchivosDeTools();
        archivos.Should().NotBeEmpty("el fence es inútil si no encuentra las tools; revisa la ruta");

        var sinGuarda = new List<string>();
        var total = 0;

        foreach (var archivo in archivos)
        {
            var fuente = File.ReadAllText(archivo);

            // El primer trozo es lo que precede a la primera tool (usings, clase): se descarta.
            // Cada trozo siguiente es el cuerpo de una tool hasta el atributo de la siguiente.
            var trozos = fuente.Split(ToolAttribute, StringSplitOptions.None).Skip(1);

            foreach (var trozo in trozos)
            {
                total++;
                if (!trozo.Contains("HasScope", StringComparison.Ordinal))
                {
                    sinGuarda.Add($"{Path.GetFileName(archivo)} :: {NombreDeTool(trozo)}");
                }
            }
        }

        total.Should().BeGreaterThan(0, "sin tools detectadas el test pasaría vacío");

        sinGuarda.Should().BeEmpty(
            $"toda tool MCP debe comprobar su scope, y {sinGuarda.Count} de {total} no lo hace: "
            + string.Join(", ", sinGuarda));
    }

    /// <summary>
    /// Cada guarda rechaza con el mismo scope que comprueba. Detecta el
    /// copy-paste que comprueba un scope y reporta otro — el mensaje diría al agente que pida
    /// un scope que no le habilitaría la tool.
    /// </summary>
    [Fact]
    public void La_guarda_rechaza_con_el_mismo_scope_que_comprueba()
    {
        var descuadres = new List<string>();

        foreach (var archivo in ArchivosDeTools())
        {
            var trozos = File.ReadAllText(archivo).Split(ToolAttribute, StringSplitOptions.None).Skip(1);

            foreach (var trozo in trozos)
            {
                var comprobados = ScopesEn(trozo, @"HasScope\(\s*McpScopes\.(\w+)").ToHashSet();
                var reportados = ScopesEn(trozo, @"InsufficientScope\(\s*McpScopes\.(\w+)").ToHashSet();

                // Una tool puede comprobar varios scopes; exigimos que lo reportado sea un
                // subconjunto de lo comprobado, no igualdad (hay guardas con OR).
                var huerfanos = reportados.Except(comprobados).ToList();
                if (huerfanos.Count > 0)
                {
                    descuadres.Add(
                        $"{Path.GetFileName(archivo)} :: {NombreDeTool(trozo)} reporta "
                        + $"{string.Join("/", huerfanos)} pero no lo comprueba");
                }
            }
        }

        descuadres.Should().BeEmpty(string.Join(" | ", descuadres));
    }

    private static IEnumerable<string> ScopesEn(string trozo, string patron)
        => Regex.Matches(trozo, patron).Select(m => m.Groups[1].Value);

    private static string NombreDeTool(string trozo)
    {
        var m = Regex.Match(trozo, @"Name\s*=\s*""([^""]+)""");
        return m.Success ? m.Groups[1].Value : "(sin nombre)";
    }

    private static string[] ArchivosDeTools()
    {
        var raiz = RaizDelRepo();
        var dir = Path.Combine(raiz, "src", "Aethra.Modules.Mcp", "Tools");
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.cs") : [];
    }

    /// <summary>Sube desde el binario hasta el directorio que contiene la solución.</summary>
    private static string RaizDelRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aethra.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException(
            "No se encontró Aethra.slnx subiendo desde " + AppContext.BaseDirectory);
    }
}
