using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// Configuración y validación de la política CORS (#87).
///
/// Permite que instalaciones remotas (LAN o dominio público HTTPS) configuren el origen de su UI
/// sin modificar código fuente, manteniendo activos los orígenes locales de desarrollo por defecto.
///
/// Reglas de seguridad:
/// - Cada entrada configurada debe ser una URI absoluta http/https sin ruta ni barra final.
/// - Con peticiones autenticadas (AllowCredentials), nunca se admite comodín ('*') ni fragmentos/consultas.
/// - Un origen no configurado queda denegado por defecto.
/// </summary>
public static class CorsBootstrap
{
    /// <summary>
    /// Orígenes locales permitidos por defecto para desarrollo sin requerir configuración adicional.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultDevelopmentOrigins =
    [
        "http://localhost:3000",
        "http://localhost:5173",
        "http://127.0.0.1:3000",
        "http://127.0.0.1:5173",
    ];

    /// <summary>
    /// Valida un origen individual según las reglas de seguridad.
    /// Retorna el origen normalizado en formato esquema://host[:puerto].
    /// </summary>
    public static string ValidateOrigin(string rawOrigin)
    {
        if (string.IsNullOrWhiteSpace(rawOrigin))
        {
            throw new InvalidOperationException("El origen CORS no puede estar vacío.");
        }

        var trimmed = rawOrigin.Trim();

        if (trimmed.Contains('*'))
        {
            throw new InvalidOperationException(
                $"El origen CORS '{trimmed}' contiene un comodín ('*'). Con AllowCredentials no se permiten comodines por seguridad.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"El origen CORS '{trimmed}' no es una URI absoluta válida (ejemplo: 'https://aethra.example.com').");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"El origen CORS '{trimmed}' tiene un esquema no permitido ('{uri.Scheme}'). Solo se admiten esquemas 'http' y 'https'.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"El origen CORS '{trimmed}' no debe contener credenciales de usuario, parámetros de consulta ('?') ni fragmentos ('#').");
        }

        if (uri.AbsolutePath != "/" || trimmed.EndsWith('/'))
        {
            throw new InvalidOperationException(
                $"El origen CORS '{trimmed}' no debe incluir una ruta ni barra final. Especifique solo esquema, host y puerto opcional (ejemplo: 'https://aethra.example.com' o 'http://192.168.1.50:3000').");
        }

        return $"{uri.Scheme}://{uri.Authority}";
    }

    /// <summary>
    /// Resuelve y valida la lista efectiva de orígenes permitidos leyendo de la configuración.
    /// Incluye los orígenes de desarrollo predeterminados y añade los orígenes configurados validados.
    /// </summary>
    public static string[] ResolveAndValidateAllowedOrigins(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var originsToValidate = new List<string>();

        // 1. Sección Cors:AllowedOrigins (soporta tanto arrays de configuración como strings simples)
        var section = configuration.GetSection("Cors:AllowedOrigins");
        var children = section.GetChildren().ToList();
        if (children.Count > 0)
        {
            foreach (var child in children)
            {
                if (!string.IsNullOrWhiteSpace(child.Value))
                {
                    originsToValidate.Add(child.Value);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(section.Value))
        {
            var split = section.Value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            originsToValidate.AddRange(split);
        }

        // 2. Soporte para variables de entorno directas documentadas en despliegues (AETHRA_PUBLIC_WEB_URL o AETHRA_PUBLIC_UI_URL)
        var publicWebUrl = configuration["AETHRA_PUBLIC_WEB_URL"] ?? configuration["AETHRA_PUBLIC_UI_URL"];
        if (!string.IsNullOrWhiteSpace(publicWebUrl))
        {
            var split = publicWebUrl.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            originsToValidate.AddRange(split);
        }

        var validatedList = new List<string>(DefaultDevelopmentOrigins);
        foreach (var raw in originsToValidate)
        {
            var validated = ValidateOrigin(raw);
            validatedList.Add(validated);
        }

        return validatedList.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Registra y configura la política CORS predeterminada de Aethra validando orígenes al arrancar.
    /// </summary>
    public static IServiceCollection AddAethraCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Validación inmediata durante el registro de servicios
        ResolveAndValidateAllowedOrigins(configuration);

        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                var allowedOrigins = ResolveAndValidateAllowedOrigins(config);
                options.AddDefaultPolicy(policy => policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
            });

        return services;
    }
}
