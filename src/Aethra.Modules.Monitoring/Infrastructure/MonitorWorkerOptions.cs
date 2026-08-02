using Aethra.Modules.Monitoring.Domain;

namespace Aethra.Modules.Monitoring.Infrastructure;

/// <summary>
/// Cadencia del <c>MonitorWorker</c>. Sección "Monitoring" (env: <c>Monitoring__TickSeconds</c>).
///
/// <para>
/// Existía como constante, y los dos README decían que era configurable. El tick es el límite
/// inferior de lo tarde que puede dispararse cualquier probe, así que es exactamente la perilla que
/// alguien busca cuando ve monitores de 30s disparando a 40 — y no estaba. Ver issue #25.
/// </para>
/// </summary>
public sealed class MonitorWorkerOptions
{
    /// <summary>Segundos entre barridos. Default 10.</summary>
    public double TickSeconds { get; set; } = TickPorDefecto;

    public const double TickPorDefecto = 10;

    /// <summary>
    /// Por debajo de un segundo el worker deja de ser un planificador y pasa a ser un bucle
    /// ocupado contra la BD, sin ganar granularidad util: el intervalo minimo de un monitor son
    /// 30 segundos.
    /// </summary>
    public const double TickMinimo = 1;

    /// <summary>
    /// El techo NO es un numero elegido: es <see cref="Monitor.MinIntervalSec"/>. Con un tick mas
    /// largo que el intervalo mas corto que un monitor puede pedir, ese monitor no puede sondearse
    /// a su ritmo por construccion — el operador habria configurado algo que el sistema no puede
    /// cumplir. Atarlo a la constante del dominio hace que la relacion sobreviva a que esta cambie.
    /// </summary>
    public static double TickMaximo => Monitor.MinIntervalSec;
}

/// <summary>Lo que se decidio sobre el tick pedido, y si hubo que tocarlo.</summary>
public readonly record struct TickResuelto(TimeSpan Efectivo, double Pedido, bool Recortado)
{
    /// <summary>
    /// Resuelve el tick efectivo diciendo SIEMPRE si tuvo que recortar. Un recorte silencioso deja
    /// al operador creyendo que configuro algo que no esta pasando, que es peor que no dejarle
    /// configurarlo: antes el valor no existia y se notaba; recortado en silencio, parece que si.
    /// </summary>
    public static TickResuelto Desde(double segundos)
    {
        if (double.IsNaN(segundos) || segundos <= 0)
        {
            // Ausente, cero o basura: no es una peticion, es la falta de una. Default sin ruido.
            return new TickResuelto(TimeSpan.FromSeconds(MonitorWorkerOptions.TickPorDefecto), segundos, false);
        }
        var efectivo = Math.Clamp(segundos, MonitorWorkerOptions.TickMinimo, MonitorWorkerOptions.TickMaximo);
        return new TickResuelto(TimeSpan.FromSeconds(efectivo), segundos, efectivo != segundos);
    }
}
