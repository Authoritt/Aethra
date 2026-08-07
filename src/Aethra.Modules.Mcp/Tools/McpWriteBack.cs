using Aethra.Modules.Mcp.Security;
using Aethra.Shared.Kernel.Results;

namespace Aethra.Modules.Mcp.Tools;

/// <summary>
/// Relectura tras una escritura: devolver lo que quedó GUARDADO en vez del argumento del llamante.
///
/// <para>
/// Los setters devolvían el parámetro recibido como si fuera el estado resultante. Si la escritura
/// se hubiera recortado, ignorado o perdido contra una escritura concurrente, la respuesta habría
/// sido idéntica byte a byte: el llamante no confirmaba nada salvo que recuerda lo que pidió.
/// Ver issue #27.
/// </para>
///
/// <para>
/// Vive AQUÍ y no copiado en cada fichero de tools por un motivo aprendido esta misma semana: una
/// revisión encontró que la valla de arquitectura tenía su propia copia del comparador que los tests
/// probaban, así que los tests pasaban sobre código que la valla nunca ejecutaba. Tres copias de esta
/// lógica serían tres sitios donde el manejo del caso "no confirmable" puede divergir en silencio.
/// </para>
/// </summary>
internal static class McpWriteBack
{
    /// <summary>
    /// Tres desenlaces, no dos: confirmado, escrito-pero-no-confirmable, y error de escritura (que
    /// resuelve el llamante antes de entrar aquí).
    ///
    /// <para>
    /// El del medio es el que un booleano aplastaría, y es justo el que hay que distinguir: la
    /// relectura puede <b>lanzar</b>, no solo devolver un <see cref="Result{T}"/> fallido — ningún
    /// behavior del pipeline convierte excepciones en Result. Sin atraparlas, un fallo transitorio
    /// de BD sale como error opaco DESPUÉS de que la escritura commiteó, y un agente que ve un error
    /// de invocación reintenta una mutación ya hecha.
    /// </para>
    /// </summary>
    /// <param name="releer">La consulta de relectura. Debe apuntar al objeto recién escrito.</param>
    /// <param name="proyectar">Proyección de lo persistido a la respuesta.</param>
    /// <param name="sinConfirmar">
    /// Respuesta cuando no se pudo releer. Recibe el motivo (tipo de excepción, o
    /// <c>"read_failed"</c>) para que quede en la respuesta y no solo en el log.
    /// </param>
    public static async Task<object> ConfirmarAsync<T>(
        Func<CancellationToken, Task<Result<T>>> releer,
        Func<T, object> proyectar,
        Func<string, object> sinConfirmar,
        CancellationToken ct)
    {
        string motivo;
        try
        {
            var leido = await releer(ct).ConfigureAwait(false);
            if (leido.IsSuccess)
            {
                return McpResponses.Ok(proyectar(leido.Value));
            }
            motivo = "read_failed";
        }
        catch (OperationCanceledException)
        {
            // La cancelación es una petición del llamante, no un fallo del sistema: propaga.
            throw;
        }
#pragma warning disable CA1031 // Cualquier fallo de la relectura es "no confirmable", nunca "no escrito".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            motivo = ex.GetType().Name;
        }

        return McpResponses.Ok(sinConfirmar(motivo));
    }

    /// <summary>
    /// Nota estándar del caso no confirmable. Dice explícitamente que NO se reintente: el llamante
    /// no puede saberlo desde la respuesta, y reintentar es la reacción natural ante una confirmación
    /// ausente.
    /// </summary>
    public const string NotaSinConfirmar =
        "La escritura fue aceptada, pero no se pudo releer para confirmar el estado resultante. "
        + "No se devuelve el valor pedido como si fuera el guardado. NO reintentes la escritura por "
        + "esto: ya commiteo.";
}
