using System.Reflection;
using Aethra.Modules.Mcp.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp;

/// <summary>
/// Punto de entrada del módulo MCP (Model Context Protocol).
///
/// Expone <c>POST /mcp</c> (transporte Streamable HTTP) que requiere autenticación por API key
/// — el handler de Identity ya valida el header <c>Authorization: Bearer aethra_...</c> y emite
/// claims con los scopes.
///
/// Diseño:
/// - Las herramientas se descubren por reflexión en el ensamblado actual (atributo
///   <c>[McpServerToolType]</c>). Cada herramienta es una instance method que recibe sus
///   dependencias por DI (incluido <c>IMcpCallerContext</c> para chequear scopes).
/// - El SDK construye una instancia nueva del tool-type por invocación. Como cada tool-type
///   es muy ligero (sólo guarda <c>IMediator</c> y <c>IMcpCallerContext</c>), esto es barato.
/// - Errores estructurados: cada tool atrapa fallos esperados y devuelve
///   <c>{ ok: false, error: { code, message, type } }</c> mediante <c>McpResponses</c>.
/// </summary>
public static class McpModule
{
    /// <summary>Ruta donde se monta el transport MCP. Coincide con la convención del SDK.</summary>
    public const string McpEndpointPath = "/mcp";

    public static IServiceCollection AddMcpModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = configuration;

        // Caller context lee de IHttpContextAccessor. Lo registramos como Scoped — cada
        // request HTTP del MCP transport tiene su propio HttpContext.
        services.AddScoped<IMcpCallerContext, HttpMcpCallerContext>();

        // MediatR ya escanea todos los assemblies de módulos en Program.cs; pero el
        // AttachDomainHandler vive aquí, así que añadimos el ensamblado actual para que
        // MediatR registre ese handler también.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(McpModule).Assembly));

        services.AddMcpServer()
            .WithHttpTransport()
            // WithToolsFromAssembly descubre todas las clases marcadas [McpServerToolType]
            // de este ensamblado y registra cada método [McpServerTool] como tool MCP.
            .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }

    /// <summary>
    /// Mapea el endpoint MCP. Debe llamarse después de <c>UseAuthentication</c>/<c>UseAuthorization</c>.
    /// El endpoint resultante exige API key — el handler de Identity ya valida el header.
    /// Si la API key no tiene el scope requerido por una tool específica, la tool devolverá
    /// <c>{ ok: false, error: { code: "insufficient_scope" } }</c> sin reventar la sesión.
    /// </summary>
    public static IEndpointRouteBuilder MapMcpModuleEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // RequireAuthorization sin args usa la default policy del host (cookie OR apikey).
        // Eso permite que la sesión cookie admin (single-user) también pueda llamar al MCP,
        // útil para una UI debug-tool integrada en el dashboard. Los agentes externos usarán
        // API key obligatoriamente porque no tienen la cookie.
        app.MapMcp(McpEndpointPath).RequireAuthorization();
        return app;
    }
}
