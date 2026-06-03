using System.Reflection;
using Aethra.Modules.Mcp.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace Aethra.Modules.Mcp;

/// <summary>
/// Punto de entrada del módulo MCP (Model Context Protocol).
///
/// Expone <c>POST /mcp</c> (transporte Streamable HTTP) que requiere autenticación por cookie
/// admin o API key. Las claims (incluyendo scopes) se capturan al iniciar la sesión MCP
/// y se propagan al loop de background del SDK via <see cref="System.Threading.AsyncLocal{T}"/>
/// — ver <see cref="McpSessionPrincipalAccessor"/> y <see cref="HttpMcpCallerContext"/> para
/// los detalles. NO se usa <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>
/// porque el handler de tools corre fuera del request HTTP del request HTTP que invoca
/// <c>tools/call</c>: el SDK consume mensajes desde un Channel en una task de background y el
/// <c>HttpContext</c> del request inicial ya fue despuesto cuando arranca la task.
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

        // Singleton — single AsyncLocal holder compartido entre todos los scopes que el SDK
        // crea por tool call. Capture() se llama 1x por sesión dentro de ConfigureSessionOptions.
        services.AddSingleton<IMcpSessionPrincipalAccessor, McpSessionPrincipalAccessor>();

        // Caller context lee del accessor singleton. Scoped porque las tools lo inyectan por
        // instancia (el SDK construye 1 tool-type por invocación).
        services.AddScoped<IMcpCallerContext, HttpMcpCallerContext>();

        // MediatR ya escanea todos los assemblies de módulos en Program.cs; pero el
        // AttachDomainHandler vive aquí, así que añadimos el ensamblado actual para que
        // MediatR registre ese handler también.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(McpModule).Assembly));

        services.AddMcpServer()
            .WithHttpTransport(opts =>
            {
                // F11.7 fix de propagación de claims: capturamos el ClaimsPrincipal del
                // HttpContext que abrió la sesión. La AsyncLocal se setea en la
                // ExecutionContext de esta callback — el SDK arranca después la task
                // background (`session.RunAsync`) que hereda esa ExecutionContext y por
                // tanto el principal. Sin esto, las tools veían un principal nulo (porque
                // el HttpContext del request inicial fue despuesto) y devolvían
                // `insufficient_scope` en todas las scope checks.
                opts.ConfigureSessionOptions = (httpContext, _, _) =>
                {
                    var accessor = httpContext.RequestServices.GetRequiredService<IMcpSessionPrincipalAccessor>();
                    accessor.Capture(httpContext.User);
                    return Task.CompletedTask;
                };
            })
            // WithToolsFromAssembly descubre todas las clases marcadas [McpServerToolType]
            // de este ensamblado y registra cada método [McpServerTool] como tool MCP.
            .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }

    /// <summary>
    /// Mapea el endpoint MCP. Debe llamarse después de <c>UseAuthentication</c>/<c>UseAuthorization</c>.
    /// El endpoint resultante exige auth (cookie OR ApiKey, via default policy). Si el caller
    /// no tiene el scope requerido por una tool específica, la tool devolverá
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
