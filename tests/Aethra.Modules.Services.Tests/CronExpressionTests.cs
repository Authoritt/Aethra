using Aethra.Modules.Services.Infrastructure.Scheduling;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Services.Tests;

/// <summary>
/// Parser cron custom de 5 campos (<see cref="CronExpression"/>) que gobierna CUÁNDO corren los
/// scheduled jobs y los backups (lo usan ScheduledJobWorker y BackupWorker). Sin cobertura previa:
/// un bug acá = jobs/backups en la hora equivocada o nunca. Cubre TryParse (formas válidas/ inválidas,
/// rangos, normalización domingo 7→0) y GetNextOccurrence (próximo tick estricto, salto de día/mes,
/// cron imposible → null). Todo en UTC para ser determinista y portable (Windows local / Linux VM).
/// </summary>
public sealed class CronExpressionTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(int y, int mo, int d, int h, int mi)
        => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0 2 * * *")]        // diario 02:00
    [InlineData("*/5 * * * *")]      // cada 5 min
    [InlineData("0,15,30,45 * * * *")] // lista
    [InlineData("0 0 * * 1-5")]      // rango dow (lun-vie)
    [InlineData("0 0 * * 0")]        // domingo como 0
    [InlineData("0 0 * * 7")]        // domingo como 7 (normaliza a 0)
    [InlineData("59 23 31 12 *")]    // límites superiores válidos
    [InlineData("  0   2  *  *  * ")] // espacios extra colapsados
    public void TryParse_accepts_valid_expressions(string expr)
    {
        CronExpression.TryParse(expr, out var cron).Should().BeTrue();
        cron.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0 2 * *")]        // 4 campos
    [InlineData("0 2 * * * *")]    // 6 campos
    [InlineData("60 * * * *")]     // minuto > 59
    [InlineData("* 24 * * *")]     // hora > 23
    [InlineData("* * 0 * *")]      // día-mes < 1
    [InlineData("* * 32 * *")]     // día-mes > 31
    [InlineData("* * * 13 *")]     // mes > 12
    [InlineData("* * * * 8")]      // dow > 7
    [InlineData("*/0 * * * *")]    // step < 1
    [InlineData("5-1 * * * *")]    // rango invertido
    [InlineData("abc * * * *")]    // no numérico
    public void TryParse_rejects_invalid_expressions(string? expr)
    {
        CronExpression.TryParse(expr, out var cron).Should().BeFalse();
        cron.Should().BeNull();
    }

    [Fact]
    public void Parse_throws_on_invalid()
    {
        var act = () => CronExpression.Parse("not a cron");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sunday_7_and_0_are_equivalent()
    {
        // Domingo 2026-01-04. Ambas expresiones deben matchear el mismo próximo tick.
        var c0 = CronExpression.Parse("0 0 * * 0");
        var c7 = CronExpression.Parse("0 0 * * 7");
        var after = At(2026, 1, 1, 12, 0); // jueves

        c0.GetNextOccurrence(after, Utc).Should().Be(c7.GetNextOccurrence(after, Utc));
        c0.GetNextOccurrence(after, Utc).Should().Be(At(2026, 1, 4, 0, 0));
    }

    [Fact]
    public void GetNextOccurrence_is_strictly_after_for_daily()
    {
        var cron = CronExpression.Parse("0 2 * * *");

        // Antes de las 02:00 del mismo día → 02:00 de hoy.
        cron.GetNextOccurrence(At(2026, 1, 1, 0, 0), Utc).Should().Be(At(2026, 1, 1, 2, 0));
        // Justo a las 02:00 → debe saltar al día siguiente (estrictamente posterior).
        cron.GetNextOccurrence(At(2026, 1, 1, 2, 0), Utc).Should().Be(At(2026, 1, 2, 2, 0));
        // Después de las 02:00 → al día siguiente.
        cron.GetNextOccurrence(At(2026, 1, 1, 3, 0), Utc).Should().Be(At(2026, 1, 2, 2, 0));
    }

    [Fact]
    public void GetNextOccurrence_every_five_minutes()
    {
        var cron = CronExpression.Parse("*/5 * * * *");

        cron.GetNextOccurrence(At(2026, 1, 1, 0, 0), Utc).Should().Be(At(2026, 1, 1, 0, 5));
        cron.GetNextOccurrence(At(2026, 1, 1, 0, 1), Utc).Should().Be(At(2026, 1, 1, 0, 5));
        cron.GetNextOccurrence(At(2026, 1, 1, 0, 5), Utc).Should().Be(At(2026, 1, 1, 0, 10));
    }

    [Fact]
    public void GetNextOccurrence_weekdays_skips_weekend()
    {
        var cron = CronExpression.Parse("0 0 * * 1-5"); // 00:00 lun-vie
        // Sábado 2026-01-03 mediodía → próximo es lunes 2026-01-05 00:00 (salta dom).
        cron.GetNextOccurrence(At(2026, 1, 3, 12, 0), Utc).Should().Be(At(2026, 1, 5, 0, 0));
    }

    [Fact]
    public void GetNextOccurrence_rolls_to_next_year_for_specific_month()
    {
        var cron = CronExpression.Parse("30 14 1 1 *"); // 1 enero 14:30
        cron.GetNextOccurrence(At(2026, 6, 1, 0, 0), Utc).Should().Be(At(2027, 1, 1, 14, 30));
    }

    [Fact]
    public void GetNextOccurrence_returns_null_for_impossible_cron()
    {
        var cron = CronExpression.Parse("0 0 31 2 *"); // 31 de febrero: nunca existe
        cron.GetNextOccurrence(At(2026, 1, 1, 0, 0), Utc).Should().BeNull();
    }
}
