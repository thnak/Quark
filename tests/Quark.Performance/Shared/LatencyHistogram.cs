namespace Quark.Performance.Shared;

/// <summary>
///     Hand-rolled percentile recorder shared by the contention/fairness/scheduling-quality/
///     backpressure/actor-lifecycle runners. Every worker gets its own thread-local sample buffer
///     so concurrent <see cref="Record" /> calls never contend with each other — the same
///     "no shared mutable hot path" lesson as <see cref="PaddedCounter" />, generalized from a
///     running sum to a growable sample list.
///     Not safe to call <see cref="Merge" /> while another thread is still calling
///     <see cref="Record" /> — always fully stop recording workers first.
/// </summary>
public sealed class LatencyHistogram : IDisposable
{
    private readonly ThreadLocal<List<double>> _perThread;

    public LatencyHistogram(int perThreadCapacityHint = 4096)
    {
        _perThread = new ThreadLocal<List<double>>(
            () => new List<double>(perThreadCapacityHint),
            trackAllValues: true);
    }

    /// <summary>Records one sample in microseconds. Callers must record consistently in this unit.</summary>
    public void Record(double microseconds) => _perThread.Value!.Add(microseconds);

    /// <summary>
    ///     Combines every thread-local buffer, sorts once, and computes percentiles by nearest-rank.
    ///     Returns <see cref="Percentiles.Empty" /> if nothing was ever recorded.
    /// </summary>
    public Percentiles Merge()
    {
        int total = 0;
        foreach (List<double> buffer in _perThread.Values)
        {
            total += buffer.Count;
        }

        if (total == 0)
        {
            return Percentiles.Empty;
        }

        var all = new double[total];
        int offset = 0;
        double sum = 0;
        foreach (List<double> buffer in _perThread.Values)
        {
            buffer.CopyTo(all, offset);
            offset += buffer.Count;
        }
        foreach (double sample in all)
        {
            sum += sample;
        }

        Array.Sort(all);

        return new Percentiles(
            all.Length,
            sum / all.Length,
            PercentileAt(all, 0.50),
            PercentileAt(all, 0.90),
            PercentileAt(all, 0.99),
            PercentileAt(all, 0.999),
            all[^1]);
    }

    private static double PercentileAt(double[] sorted, double p)
    {
        int index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    public void Dispose() => _perThread.Dispose();
}

/// <summary>Percentile summary of a <see cref="LatencyHistogram" />, in microseconds.</summary>
public readonly record struct Percentiles(int Count, double Mean, double P50, double P90, double P99, double P999, double Max)
{
    public static readonly Percentiles Empty = new(0, 0, 0, 0, 0, 0, 0);

    public override string ToString()
        => $"n={Count:N0} mean={Mean:N1}us p50={P50:N1}us p90={P90:N1}us p99={P99:N1}us p999={P999:N1}us max={Max:N1}us";
}
