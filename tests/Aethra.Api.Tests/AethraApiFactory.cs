using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Aethra.Api.Tests;

/// <summary>
/// OT-012 — el <c>Program</c> REAL de la API (mismos endpoints, mismo pipeline) con tres
/// sustituciones de infraestructura y nada mas:
/// <list type="bullet">
/// <item>Sin BD: la cadena apunta a un puerto cerrado y se quitan los hosted services (los
/// endpoints probados aqui no tocan la BD antes de responder lo que se mide).</item>
/// <item>La config de YARP sale de memoria con la MISMA forma que <c>DatabaseProxyConfigProvider</c>
/// (Hosts=[host], "/{**catch-all}" Order 0; prefijo "/api" Order -4), copiando los hosts reales:
/// yunke, ekippo (con su /api propio), relaycore y el propio panel (aethra-web).</item>
/// <item>El forwarder de YARP habla con un stub que contesta <c>app:&lt;cluster&gt; &lt;metodo&gt; &lt;ruta&gt;</c>:
/// si la respuesta empieza por <c>app:</c>, la peticion la recibio la APP, no Aethra.</item>
/// </list>
/// </summary>
public class AethraApiFactory : WebApplicationFactory<Program>
{
    public const string PanelHost = "aethra.test";
    public const string PeerHeader = "X-Test-Peer";
    public const string BridgeGateway = "172.18.0.1";
    public const string OtherContainer = "172.18.0.7";

    // Program lee la cadena de conexion y Panel:Hosts directamente de builder.Configuration;
    // las variables de entorno las ve con seguridad antes de Build().
    static AethraApiFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Aethra",
            "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1;Command Timeout=1");
        Environment.SetEnvironmentVariable("DataProtection__KeyDir",
            Path.Combine(Path.GetTempPath(), "aethra-ot012-tests-keys"));
        Environment.SetEnvironmentVariable("Tls__AccountEmail", "tests@aethra.local");
        Environment.SetEnvironmentVariable("Tls__UseStaging", "true");
        Environment.SetEnvironmentVariable("Panel__Hosts__0", PanelHost);
        // Gateway EXACTO del bridge (como en produccion): par local y del tunel. El resto de
        // 172.16/12 (otros contenedores) no es de confianza.
        Environment.SetEnvironmentVariable("Panel__LocalPeers__0", BridgeGateway);
        Environment.SetEnvironmentVariable("Panel__LoginRateLimit__PermitLimit", "3");
        Environment.SetEnvironmentVariable("Panel__LoginRateLimit__WindowSeconds", "600");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing": evita la migracion automatica de Development (no hay BD). El caso
        // Development de OpenAPI se cubre aparte (OpenApiAccessTests).
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IProxyConfigProvider>();
            services.AddSingleton<IProxyConfigProvider>(new StubProxyConfigProvider(AppRoutes()));
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory, StubForwarderFactory>();

            // TestServer no rellena RemoteIpAddress: la cabecera de test simula el par TCP. Va en
            // un IStartupFilter, es decir, ANTES de todo el pipeline de Program (como la red real).
            services.AddSingleton<IStartupFilter, TestPeerStartupFilter>();
        });
    }

    private static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) AppRoutes()
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();
        void Add(string id, string host, string prefix, string cluster)
        {
            var (path, order) = prefix == "/" ? ("/{**catch-all}", 0) : ($"{prefix}/{{**catch-all}}", -prefix.Length);
            routes.Add(new RouteConfig
            {
                RouteId = id,
                ClusterId = cluster,
                Order = order,
                Match = new RouteMatch { Hosts = [host], Path = path },
            });
            if (clusters.All(c => c.ClusterId != cluster))
            {
                clusters.Add(new ClusterConfig
                {
                    ClusterId = cluster,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["primary"] = new() { Address = $"http://{cluster}.internal/" },
                    },
                });
            }
        }
        Add("yunke", "yunke.test", "/", "yunke-web");
        Add("yunke-api", "yunke-api.test", "/", "yunke-api");
        Add("ekippo", "ekippo.test", "/", "ekippo-web");
        Add("ekippo-api", "ekippo.test", "/api", "ekippo-api");
        Add("relaycore", "relaycore.test", "/", "relaycore");
        Add("panel-web", PanelHost, "/", "aethra-web");
        return (routes, clusters);
    }

    private sealed class TestPeerStartupFilter : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, n) =>
            {
                var raw = ctx.Request.Headers[PeerHeader].ToString();
                ctx.Connection.RemoteIpAddress = IPAddress.TryParse(raw, out var ip) ? ip : IPAddress.Loopback;
                return n(ctx);
            });
            next(app);
        };
    }

    private sealed class StubProxyConfigProvider(
        (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) config) : IProxyConfigProvider
    {
        private readonly StubConfig _config = new(config.Routes, config.Clusters);
        public IProxyConfig GetConfig() => _config;

        private sealed class StubConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
            : IProxyConfig
        {
            public IReadOnlyList<RouteConfig> Routes { get; } = routes;
            public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
            public Microsoft.Extensions.Primitives.IChangeToken ChangeToken { get; } =
                new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
        }
    }

    private sealed class StubForwarderFactory : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
            => new(new AppStubHandler(), disposeHandler: true);
    }

    private sealed class AppStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cluster = request.RequestUri!.Host.Replace(".internal", string.Empty, StringComparison.Ordinal);
            var body = $"app:{cluster} {request.Method} {request.RequestUri.AbsolutePath}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/plain"),
            });
        }
    }
}
