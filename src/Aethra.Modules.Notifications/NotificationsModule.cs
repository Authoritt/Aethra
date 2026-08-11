using Aethra.Modules.Notifications.Domain;
using Aethra.Modules.Notifications.Infrastructure;
using Aethra.Modules.Notifications.Infrastructure.Dispatch;
using Aethra.Modules.Notifications.Infrastructure.Email;
using Aethra.Modules.Notifications.Infrastructure.Handlers;
using Aethra.Modules.Notifications.Infrastructure.Security;
using Aethra.Modules.Notifications.Presentation;
using Aethra.Shared.Infrastructure.Http;
using Aethra.Shared.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aethra.Modules.Notifications;

/// <summary>
/// Punto de entrada del modulo Notifications.
///
/// Wiring desde apps/api/Program.cs:
/// - <see cref="AddNotificationsModule"/> en builder.Services para DI.
/// - <see cref="MapNotificationsModuleEndpoints"/> tras UseAuthorization() para rutas REST.
///
/// Registra:
/// - DbContext + outbox writer/store + dispatcher (shared infra).
/// - <see cref="INotificationConfigCodec"/> (DataProtection).
/// - <see cref="IEmailSender"/> (SMTP built-in).
/// - <see cref="NotificationDispatcher"/> BackgroundService (loop cada 5s).
/// - <see cref="NotificationEventDispatcher"/> helper para listeners cross-module.
/// </summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("Aethra")
            ?? throw new InvalidOperationException("ConnectionStrings:Aethra no configurado.");

        services.AddAethraModuleDbContext<NotificationsDbContext>(conn);

        services.AddSingleton<INotificationConfigCodec, DataProtectionNotificationConfigCodec>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<NotificationEventDispatcher>();

        // HttpClient named para dispatcher (Slack/Discord/Telegram/Webhook).
        services.AddHttpClient("notifications", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        // Un canal webhook lleva URL, metodo y cabeceras elegidos por quien lo configura: sin esto,
        // el plano de control es un proxy hacia loopback, la malla privada y los metadatos de la nube.
        .GuardOutboundDestinations();

        // Singleton para que el BackgroundService pueda inyectarse en handlers transient (TestChannel)
        // sin crear scopes nuevos. Internamente abre un scope por batch para acceder al DbContext.
        services.AddSingleton<NotificationDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());

        // Retención del historial de envíos (evita el crecimiento ilimitado) — sección "Notifications".
        services.Configure<NotificationsRetentionOptions>(configuration.GetSection("Notifications"));
        services.AddHostedService<NotificationDeliveryRetentionWorker>();

        return services;
    }

    public static IEndpointRouteBuilder MapNotificationsModuleEndpoints(this IEndpointRouteBuilder app)
        => app.MapNotificationsEndpoints();
}
