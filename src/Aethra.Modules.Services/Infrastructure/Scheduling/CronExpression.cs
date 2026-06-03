using System.Globalization;

namespace Aethra.Modules.Services.Infrastructure.Scheduling;

/// <summary>
/// F12.1A — parser cron minimalista de 5 campos: <c>minute hour day month dayOfWeek</c>.
/// Soporta:
/// <list type="bullet">
///   <item><c>*</c> — todos los valores.</item>
///   <item><c>N</c> — valor especifico (ej. <c>30</c> en minutos).</item>
///   <item><c>*/N</c> — cada N (ej. <c>*/5</c> en minutos = cada 5 minutos).</item>
///   <item><c>A,B,C</c> — lista de valores (ej. <c>0,15,30,45</c>).</item>
///   <item><c>A-B</c> — rango (ej. <c>1-5</c> en dayOfWeek = lunes a viernes).</item>
/// </list>
///
/// No soporta nombres mes/dia (<c>MON</c>, <c>JAN</c>) — usar numeros (1-7 lun-dom o 1-12).
/// Rango dayOfWeek: <c>0</c> y <c>7</c> ambos son domingo (compat cron clasico).
/// El metodo principal es <see cref="GetNextOccurrence"/> que calcula el proximo tick
/// estrictamente posterior a <paramref name="after"/>.
/// </summary>
public sealed class CronExpression
{
    private readonly HashSet<int> _minutes;
    private readonly HashSet<int> _hours;
    private readonly HashSet<int> _daysOfMonth;
    private readonly HashSet<int> _months;
    private readonly HashSet<int> _daysOfWeek;
    private readonly string _raw;

    private CronExpression(
        HashSet<int> minutes, HashSet<int> hours, HashSet<int> daysOfMonth,
        HashSet<int> months, HashSet<int> daysOfWeek, string raw)
    {
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
        _raw = raw;
    }

    public string Expression => _raw;

    /// <summary>
    /// Intenta parsear <paramref name="expression"/>. Devuelve <c>false</c> si el formato
    /// no es valido (numero de campos, valores fuera de rango, etc.).
    /// </summary>
    public static bool TryParse(string? expression, out CronExpression? cron)
    {
        cron = null;
        if (string.IsNullOrWhiteSpace(expression)) { return false; }
        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) { return false; }

        if (!TryParseField(fields[0], 0, 59, out var minutes)) { return false; }
        if (!TryParseField(fields[1], 0, 23, out var hours)) { return false; }
        if (!TryParseField(fields[2], 1, 31, out var dom)) { return false; }
        if (!TryParseField(fields[3], 1, 12, out var months)) { return false; }
        if (!TryParseField(fields[4], 0, 7, out var dow)) { return false; }
        // Normaliza domingo: 7 → 0 (cron clasico).
        if (dow.Remove(7)) { dow.Add(0); }

        cron = new CronExpression(minutes, hours, dom, months, dow, expression.Trim());
        return true;
    }

    public static CronExpression Parse(string expression)
    {
        if (!TryParse(expression, out var cron) || cron is null)
        {
            throw new ArgumentException($"Cron expression invalida: '{expression}'.", nameof(expression));
        }
        return cron;
    }

    /// <summary>
    /// Calcula el proximo tick estrictamente posterior a <paramref name="after"/> en la zona
    /// <paramref name="timeZone"/>. Devuelve <c>null</c> si en los proximos 4 años no hay match
    /// (caso degenerado: cron imposible como <c>0 0 31 2 *</c>).
    /// </summary>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone)
    {
        // Operamos en la zona local; al final convertimos a UTC offset.
        var local = TimeZoneInfo.ConvertTime(after, timeZone);
        // Avanzamos al siguiente minuto cerrado.
        local = new DateTimeOffset(
            local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Offset)
            .AddMinutes(1);

        var safety = local.AddYears(4);
        while (local < safety)
        {
            if (!_months.Contains(local.Month))
            {
                // Saltar al primer dia del proximo mes.
                local = StartOfNextMonth(local);
                continue;
            }
            if (!_daysOfMonth.Contains(local.Day) || !_daysOfWeek.Contains((int)local.DayOfWeek))
            {
                local = local.Date.AddDays(1);
                local = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
                continue;
            }
            if (!_hours.Contains(local.Hour))
            {
                local = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset)
                    .AddHours(1);
                continue;
            }
            if (!_minutes.Contains(local.Minute))
            {
                local = local.AddMinutes(1);
                continue;
            }
            // Match. Convertimos a la offset del timezone.
            var matched = new DateTimeOffset(
                local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Offset);
            return TimeZoneInfo.ConvertTime(matched, TimeZoneInfo.Utc);
        }
        return null;
    }

    private static DateTimeOffset StartOfNextMonth(DateTimeOffset d)
    {
        var year = d.Year;
        var month = d.Month + 1;
        if (month > 12)
        {
            year++;
            month = 1;
        }
        return new DateTimeOffset(year, month, 1, 0, 0, 0, d.Offset);
    }

    private static bool TryParseField(string field, int min, int max, out HashSet<int> values)
    {
        values = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(field)) { return false; }
        var parts = field.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in parts)
        {
            var part = raw.Trim();
            if (part == "*")
            {
                for (var i = min; i <= max; i++) { values.Add(i); }
                continue;
            }
            // */N — step expression.
            if (part.StartsWith("*/", StringComparison.Ordinal))
            {
                if (!int.TryParse(part[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var step) || step < 1)
                {
                    return false;
                }
                for (var i = min; i <= max; i += step) { values.Add(i); }
                continue;
            }
            // A-B range.
            var dashIdx = part.IndexOf('-', StringComparison.Ordinal);
            if (dashIdx > 0)
            {
                if (!int.TryParse(part[..dashIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo)
                    || !int.TryParse(part[(dashIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hi)
                    || lo < min || hi > max || lo > hi)
                {
                    return false;
                }
                for (var i = lo; i <= hi; i++) { values.Add(i); }
                continue;
            }
            // Numero simple.
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                || n < min || n > max)
            {
                return false;
            }
            values.Add(n);
        }
        return values.Count > 0;
    }
}
