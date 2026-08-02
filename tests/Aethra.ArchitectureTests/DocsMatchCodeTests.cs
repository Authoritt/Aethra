using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Aethra.ArchitectureTests;

/// <summary>
/// Lo que los documentos prometen existe en el código.
///
/// <para>
/// Nace de dos fallos reales encontrados el mismo día, con la misma causa: el README anunciaba
/// <c>aethra_trigger_build</c>, <c>aethra_trigger_deployment</c> y <c>aethra_set_secrets</c>
/// —ninguna existe— y la guía de migración desde Coolify daba comandos <c>curl</c> contra
/// <c>/api/applications</c>, que no está registrada. Ninguno de los dos era una errata: eran
/// documentos escritos AL LADO del código en vez de derivados de él, y nada los comparaba
/// porque compararlos no era trabajo de nadie.
/// </para>
///
/// <para>
/// Ahora lo es. El coste de un documento que miente no lo paga quien lo escribe: lo paga el
/// primer desconocido que lo sigue, que además no puede distinguir entre "me equivoqué yo" y
/// "la documentación está mal", y concluye razonablemente que el proyecto está abandonado.
/// </para>
///
/// <para>
/// <b>Lo que estas pruebas NO comprueban</b>, dicho aquí para que el verde no prometa de más:
/// que los parámetros, los cuerpos JSON o los verbos HTTP documentados sean correctos. Solo
/// que el NOMBRE existe. Un documento puede seguir describiendo mal una tool que sí existe.
/// </para>
/// </summary>
public sealed class DocsMatchCodeTests
{
    private static readonly string[] Documentos =
    [
        "README.md", "README.es.md", "CONTRIBUTING.md", "CHANGELOG.md",
        "docs/migration-from-coolify.md", "docs/F13-compose-native-deploy.md",
    ];

    /// <summary>
    /// Rutas que se documentan sin ser endpoints propios: pertenecen al frontend, a terceros,
    /// o son ejemplos genéricos. Cada exclusión lleva su motivo — una lista de excepciones sin
    /// motivos se convierte en el sitio donde se esconden los fallos.
    /// </summary>
    private static readonly Dictionary<string, string> RutasExentas = new()
    {
        ["/api/mcp"] = "endpoint de otra plataforma citado como ejemplo, no nuestro",
        ["/api/v1"] = "prefijo generico usado al hablar de APIs de terceros",
        ["/api/auth"] = "de una app DESPLEGADA por Aethra (ekippo), no de Aethra. Este es el",
        // limite del fence y conviene tenerlo escrito: no sabe distinguir una ruta NUESTRA
        // de la de una aplicacion que operamos. Si aparece un falso positivo asi, va aqui
        // CON su motivo; una exencion sin motivo es donde se esconde el siguiente fallo.
    };

    [Fact]
    public void Toda_tool_mcp_nombrada_en_los_docs_existe()
    {
        var declaradas = ToolsDeclaradasEnCodigo();
        declaradas.Should().NotBeEmpty("sin tools detectadas la prueba pasaria vacia");

        var fantasmas = new List<string>();
        foreach (var (doc, texto) in DocumentosLegibles())
        {
            foreach (Match m in Regex.Matches(texto, @"\baethra_[a-z0-9_]+\b"))
            {
                if (!declaradas.Contains(m.Value))
                {
                    fantasmas.Add($"{doc}: {m.Value}");
                }
            }
        }

        fantasmas.Distinct().Should().BeEmpty(
            "los docs no deben nombrar tools inexistentes; sobran: "
            + string.Join(", ", fantasmas.Distinct()));
    }

    [Fact]
    public void Todo_grupo_de_rutas_nombrado_en_los_docs_esta_registrado()
    {
        var registrados = GruposRegistrados();
        registrados.Should().NotBeEmpty("sin grupos detectados la prueba pasaria vacia");

        var fantasmas = new List<string>();
        foreach (var (doc, texto) in DocumentosLegibles())
        {
            foreach (Match m in Regex.Matches(texto, @"/api/[a-z0-9-]+"))
            {
                var grupo = m.Value;
                if (RutasExentas.ContainsKey(grupo) || registrados.Contains(grupo))
                {
                    continue;
                }
                fantasmas.Add($"{doc}: {grupo}");
            }
        }

        fantasmas.Distinct().Should().BeEmpty(
            "los docs no deben dar curl contra rutas sin registrar; sobran: "
            + string.Join(", ", fantasmas.Distinct()));
    }

    // ---------- lectura del código ----------

    private static HashSet<string> ToolsDeclaradasEnCodigo()
    {
        var dir = Path.Combine(RaizDelRepo(), "src", "Aethra.Modules.Mcp", "Tools");
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(dir))
        {
            return set;
        }
        foreach (var f in Directory.GetFiles(dir, "*.cs"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"Name\s*=\s*""(aethra_[a-z0-9_]+)"""))
            {
                set.Add(m.Groups[1].Value);
            }
        }
        return set;
    }

    /// <summary>
    /// Prefijos de grupo realmente montados. Se leen tanto <c>MapGroup("/api/x")</c> como los
    /// <c>MapGet("/api/x/...")</c> sueltos, porque el proyecto usa las dos formas.
    /// </summary>
    private static HashSet<string> GruposRegistrados()
    {
        var raiz = RaizDelRepo();
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var carpeta in new[] { "src", "apps" })
        {
            var d = Path.Combine(raiz, carpeta);
            if (!Directory.Exists(d))
            {
                continue;
            }
            foreach (var f in Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"Map\w+\(\s*""(/api/[a-z0-9-]+)"))
                {
                    set.Add(m.Groups[1].Value);
                }
            }
        }
        return set;
    }

    private static IEnumerable<(string doc, string texto)> DocumentosLegibles()
    {
        var raiz = RaizDelRepo();
        foreach (var rel in Documentos)
        {
            var p = Path.Combine(raiz, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p))
            {
                yield return (rel, File.ReadAllText(p));
            }
        }
    }

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
