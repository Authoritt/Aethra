using System.Text.RegularExpressions;

namespace Aethra.Modules.Deployments.Webhooks;

/// <summary>
/// Matchea paths modificados de un commit contra los <c>WatchPaths</c> de una Application.
/// Soporta globs tipo <c>backend/**</c>, <c>**/*.cs</c>, <c>docs/*.md</c>.
///
/// Convención: lista vacía de WatchPaths significa "siempre matchea" (deploy ante cualquier cambio
/// en la rama, comportamiento por defecto cuando no hay restricción).
/// </summary>
public static class WatchPathMatcher
{
    public static bool AnyMatches(IReadOnlyCollection<string> affectedPaths,
        IReadOnlyCollection<string> watchPatterns)
    {
        if (watchPatterns.Count == 0)
        {
            return true;
        }
        if (affectedPaths.Count == 0)
        {
            return false;
        }
        foreach (var pattern in watchPatterns)
        {
            var regex = GlobToRegex(pattern);
            foreach (var p in affectedPaths)
            {
                if (regex.IsMatch(p))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Convierte un glob estilo gitignore a un regex. Soporta:
    ///   <c>**</c> → matchea cualquier número de paths
    ///   <c>*</c>  → matchea cualquier secuencia EXCEPTO <c>/</c>
    ///   <c>?</c>  → un único char no-<c>/</c>
    /// </summary>
    private static Regex GlobToRegex(string glob)
    {
        var p = glob.Replace('\\', '/').Trim().TrimStart('/');
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            if (c == '*')
            {
                if (i + 1 < p.Length && p[i + 1] == '*')
                {
                    // "**/" matchea CERO o más segmentos (semántica gitignore): la barra es
                    // opcional para que "**/x" también matchee "x" en la raíz. Un "**" suelto
                    // (p.ej. al final de "backend/**") matchea cualquier cosa, incluido "/".
                    if (i + 2 < p.Length && p[i + 2] == '/')
                    {
                        sb.Append("(?:.*/)?");
                        i += 2;
                    }
                    else
                    {
                        sb.Append(".*");
                        i++;
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else if (c is '.' or '+' or '(' or ')' or '|' or '^' or '$' or '{' or '}' or '[' or ']' or '\\')
            {
                sb.Append('\\').Append(c);
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
