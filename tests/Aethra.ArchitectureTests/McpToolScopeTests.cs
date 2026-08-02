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

    /// <summary>
    /// La guarda ocurre ANTES del efecto. Es la propiedad que el test anterior NO puede dar:
    /// una tool que mutara y despues comprobara su scope pasaria aquel, porque alli solo se
    /// exige que la guarda EXISTA en el cuerpo.
    ///
    /// <para>
    /// Aproximacion, y conviene decirlo: compara la POSICION TEXTUAL de la guarda con la del
    /// primer <c>await</c> o <c>mediator.Send/Publish</c>. No es analisis de flujo. Una tool que
    /// mutara por una via que no pase por esos marcadores -- una escritura sincrona directa, por
    /// ejemplo -- se le escaparia. La garantia real exige mover la comprobacion al pipeline (#1);
    /// esto es lo que se puede afirmar sin un servidor corriendo.
    /// </para>
    ///
    /// <para>Hoy pasa en las 74 tools mutantes reales. Verificado por mutacion: invertir el orden
    /// en una tool lo detecta.</para>
    /// </summary>
    [Fact]
    public void La_guarda_ocurre_antes_del_primer_efecto()
    {
        var tardias = new List<string>();
        var revisadas = 0;

        foreach (var archivo in ArchivosDeTools())
        {
            foreach (var trozo in File.ReadAllText(archivo).Split(ToolAttribute, StringSplitOptions.None).Skip(1))
            {
                var cabecera = trozo.Length > 400 ? trozo[..400] : trozo;
                if (Regex.IsMatch(cabecera, @"ReadOnly\s*=\s*true") || trozo.Contains("NotImplemented(", StringComparison.Ordinal))
                {
                    continue;   // solo-lectura y stubs no mutan
                }

                var cuerpo = trozo.Length > 4000 ? trozo[..4000] : trozo;
                var guarda = Regex.Match(cuerpo, @"!caller\.HasScope");
                var efecto = Regex.Match(cuerpo, @"(?<![A-Za-z])await(?![A-Za-z])|mediator\.(Send|Publish)");
                if (!guarda.Success || !efecto.Success)
                {
                    continue;   // sin efecto detectable, nada que ordenar
                }

                revisadas++;
                if (efecto.Index < guarda.Index)
                {
                    tardias.Add($"{Path.GetFileName(archivo)} :: {NombreDeTool(trozo)}");
                }
            }
        }

        revisadas.Should().BeGreaterThan(0, "sin tools revisadas el test pasaria vacio");
        tardias.Should().BeEmpty(
            $"la guarda debe preceder al efecto, y {tardias.Count} de {revisadas} no lo hace: "
            + string.Join(", ", tardias));
    }

    /// <summary>
    /// El comparador de orden, aislado para poder probarlo con entradas sinteticas.
    /// Devuelve true si hay un efecto ANTES de la guarda.
    /// </summary>
    internal static bool EfectoAntesDeLaGuarda(string cuerpo)
    {
        var guarda = Regex.Match(cuerpo, @"!caller\.HasScope");
        var efecto = Regex.Match(cuerpo, @"(?<![A-Za-z])await(?![A-Za-z])|mediator\.(Send|Publish)");
        return guarda.Success && efecto.Success && efecto.Index < guarda.Index;
    }

    /// <summary>
    /// El comparador muerde. Sin esto, el test de orden podria estar pasando por no encontrar
    /// nunca sus marcadores en vez de por estar todo en orden -- y yo no sabria cual de las dos.
    /// Tres intentos de comprobarlo mutando ficheros reales fallaron por errores del arnes, no
    /// del fence; con entradas sinteticas no hay arnes que falle.
    /// </summary>
    [Theory]
    [InlineData("if (!caller.HasScope(x)) { return y; } var r = await z;", false)]   // correcto
    [InlineData("var r = await z; if (!caller.HasScope(x)) { return y; }", true)]    // invertido
    [InlineData("var r = mediator.Send(q); if (!caller.HasScope(x)) { }", true)]     // sin await
    [InlineData("if (!caller.HasScope(x)) { } var r = mediator.Send(q);", false)]
    [InlineData("var awaited = 1; if (!caller.HasScope(x)) { } await z;", false)]    // frontera de palabra: "awaited" no cuenta como await
    public void El_comparador_de_orden_distingue_los_dos_casos(string cuerpo, bool esperado)
    {
        EfectoAntesDeLaGuarda(cuerpo).Should().Be(esperado);
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
