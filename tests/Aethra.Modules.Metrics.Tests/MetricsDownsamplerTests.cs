using Aethra.Modules.Metrics.UseCases.Queries;
using FluentAssertions;
using Xunit;

namespace Aethra.Modules.Metrics.Tests;

/// <summary>
/// <see cref="MetricsDownsampler.Downsample"/> — reduce series largas a &lt;= maxPoints buckets promediando,
/// para servir 24h+ sin miles de puntos. Puro, sin BD.
/// </summary>
public sealed class MetricsDownsamplerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);

    private static List<VmMetricPoint> Series(int count, double cpu = 50)
    {
        var list = new List<VmMetricPoint>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new VmMetricPoint(T0.AddSeconds(i * 5), cpu, 100, 200, 0, 0, 10, 20));
        }
        return list;
    }

    [Fact]
    public void Returns_input_unchanged_when_within_limit()
    {
        var s = Series(50);
        MetricsDownsampler.Downsample(s, 240).Should().BeSameAs(s);
    }

    [Fact]
    public void Reduces_to_at_most_max_points()
    {
        var result = MetricsDownsampler.Downsample(Series(17280), 240); // 24h @ 5s
        result.Count.Should().BeLessThanOrEqualTo(240);
        result.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Preserves_chronological_order_and_uses_bucket_end_timestamp()
    {
        var result = MetricsDownsampler.Downsample(Series(1000), 100);
        result.Should().BeInAscendingOrder(p => p.Timestamp);
        result[^1].Timestamp.Should().Be(T0.AddSeconds(999 * 5)); // último punto = fin de la serie
    }

    [Fact]
    public void Averages_values_within_buckets()
    {
        // 10 puntos con cpu alternando 0/100 → promedio global ~50; cada bucket promedia.
        var list = new List<VmMetricPoint>();
        for (var i = 0; i < 10; i++)
        {
            list.Add(new VmMetricPoint(T0.AddSeconds(i), i % 2 == 0 ? 0 : 100, 100, 200, 0, 0, 0, 0));
        }
        var result = MetricsDownsampler.Downsample(list, 5); // bucketSize=2 → cada bucket {0,100} → 50
        result.Should().HaveCount(5);
        result.Should().OnlyContain(p => p.CpuPercent == 50);
    }

    [Fact]
    public void Handles_empty_series()
        => MetricsDownsampler.Downsample([], 240).Should().BeEmpty();
}
