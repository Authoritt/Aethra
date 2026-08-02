using Aethra.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Api.Bootstrap;

/// <summary>
/// Dos sondas, separadas a propósito, porque responden preguntas distintas.
///
/// <para>
/// <b>/health — vivacidad.</b> "El proceso está escuchando." No toca nada externo y por eso no
/// puede fallar por una dependencia caída: es lo que un orquestador usa para decidir si
/// REINICIAR. Devuelve <c>ok</c> siempre que haya alguien para responder, y eso es todo lo que
/// promete.
/// </para>
///
/// <para>
/// <b>/health/ready — preparación.</b> "Puedo atender tráfico." Abre la base de datos de verdad.
/// Es lo que un balanceador o un <c>depends_on: service_healthy</c> debe consultar.
/// </para>
///
/// <para>
/// Antes había una sola ruta llamada <c>/health</c> que devolvía <c>status = "ok"</c> como
/// literal, sin comprobar nada, y el compose no la consumía. Respondía <c>ok</c> con Postgres
/// inalcanzable, con las migraciones a medias y con el proveedor de rutas del proxy roto: un
/// campo de estado escrito AL LADO de la cosa en vez de derivado de ella — justo lo que esta
/// plataforma existe para quitarle a otros, servido desde su propia puerta.
/// </para>
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            probe = "liveness",
            service = "aethra-api",
            time = DateTimeOffset.UtcNow,
            version = typeof(HealthEndpoints).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        }))
        .WithName("Health")
        .WithTags("System")
        .AllowAnonymous();

        app.MapGet("/health/ready", async (SharedDbContext db, CancellationToken ct) =>
        {
            // CanConnectAsync abre una conexión real. El resultado NO se cachea: una sonda que
            // recuerda su último éxito informa del pasado, que es justo lo que no se le pregunta.
            var inicio = DateTimeOffset.UtcNow;
            bool baseDatos;
            string? detalle = null;
            try
            {
                baseDatos = await db.Database.CanConnectAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                baseDatos = false;
                detalle = ex.GetType().Name;   // el tipo, no el mensaje: puede llevar credenciales
            }

            var cuerpo = new
            {
                status = baseDatos ? "ready" : "not_ready",
                probe = "readiness",
                service = "aethra-api",
                database = baseDatos ? "up" : "down",
                error = detalle,
                elapsedMs = (int)(DateTimeOffset.UtcNow - inicio).TotalMilliseconds,
                time = DateTimeOffset.UtcNow,
            };

            // 503, no 200-con-campo: un cuerpo que dice "not_ready" bajo un 200 lo ignora
            // cualquier balanceador, y la sonda vuelve a ser decorativa.
            return baseDatos ? Results.Ok(cuerpo) : Results.Json(cuerpo, statusCode: 503);
        })
        .WithName("HealthReady")
        .WithTags("System")
        .AllowAnonymous();

        return app;
    }
}
