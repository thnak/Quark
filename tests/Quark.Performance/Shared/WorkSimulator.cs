using System.Diagnostics;

namespace Quark.Performance.Shared;

/// <summary>
///     Simulates a fixed amount of CPU-bound grain work for the contention/fairness/scheduling-
///     quality/backpressure runners.
/// </summary>
public static class WorkSimulator
{
    /// <summary>
    ///     Busy-spins for approximately <paramref name="microseconds" />, deliberately NOT
    ///     <c>Task.Delay</c>. A drain worker awaiting <c>Task.Delay</c> yields its thread back to
    ///     the pool mid-drain, which would understate the cost of real CPU-bound grain work
    ///     actually occupying a scheduler worker — exactly the property the fairness, scheduling-
    ///     quality, and backpressure runners need held constant to be meaningful. <c>Task.Delay</c>'s
    ///     coarse timer granularity (roughly 1ms+ on Linux, ~15ms on Windows) also can't represent
    ///     the tens-of-microseconds range these benchmarks vary <c>--work-us</c> over.
    /// </summary>
    public static void BusySpinMicroseconds(int microseconds)
    {
        if (microseconds <= 0)
        {
            return;
        }

        long targetTicks = (long)(microseconds * (Stopwatch.Frequency / 1_000_000.0));
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetTimestamp() - start < targetTicks)
        {
            Thread.SpinWait(16);
        }
    }
}
