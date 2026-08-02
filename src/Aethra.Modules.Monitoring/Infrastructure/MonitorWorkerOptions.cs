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
    /// <summary>
    /// Segundos entre barridos, SIN LIGAR a <c>double</c> a proposito. Una env definida y vacia
    /// (<c>Monitoring__TickSeconds=</c>, lo normal en un .env editado a medias) hace que el binder
    /// reviente al convertir, y esa excepcion saldria dentro de <c>ExecuteAsync</c> antes del primer
    /// await: el arranque entero caido por una variable vacia. Se liga como texto y se interpreta
    /// aqui, donde "vacio" puede significar lo que de verdad significa — que nadie pidio nada.
    /// </summary>
    public string? TickSeconds { get; set; }

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

    /// <summary>
    /// Cuanto se le permite a un sondeo adelantarse a su hora, y por que hace falta.
    ///
    /// <para>
    /// El tick no es solo cada cuanto se barre: es la <b>rejilla</b> de instantes en los que un
    /// sondeo puede ocurrir. Un monitor solo se mira en multiplos del tick, asi que su cadencia real
    /// es el primer multiplo que alcanza su intervalo. Y como <c>LastCheckedAt</c> se graba al
    /// TERMINAR el probe — milisegundos despues del tick que lo lanzo — el multiplo exacto se queda
    /// corto por ese epsilon y el sondeo se salta hasta el siguiente. Un monitor de 30s con tick de
    /// 10s sale cada 40; con tick de 30, cada 60. La mitad de la cadencia anunciada, en silencio.
    /// </para>
    ///
    /// <para>
    /// Medio tick de holgura lo cierra: absorbe cualquier epsilon menor que el, y acota el adelanto
    /// a menos de lo que el operador ya acepto como granularidad al elegir el tick.
    /// </para>
    /// </summary>
    public static TimeSpan ToleranciaDeRejilla(TimeSpan tick) => tick / 2;
}

/// <summary>Lo que se decidio sobre el tick pedido, y si hubo que tocarlo.</summary>
public readonly record struct TickResuelto(TimeSpan Efectivo, double Pedido, bool Recortado)
{
    /// <summary>
    /// Resuelve el tick efectivo diciendo SIEMPRE si tuvo que recortar. Un recorte silencioso deja
    /// al operador creyendo que configuro algo que no esta pasando, que es peor que no dejarle
    /// configurarlo: antes el valor no existia y se notaba; recortado en silencio, parece que si.
    /// </summary>
    /// <summary>
    /// Interpreta el valor crudo de configuracion. Vacio, en blanco o ilegible = nadie pidio nada:
    /// default y silencio. Nunca lanza — esto corre en la ruta de arranque.
    /// </summary>
    public static TickResuelto Desde(string? crudo)
        => string.IsNullOrWhiteSpace(crudo)
           || !double.TryParse(crudo, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var segundos)
            ? new TickResuelto(TimeSpan.FromSeconds(MonitorWorkerOptions.TickPorDefecto), 0, false)
            : Desde(segundos);

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
