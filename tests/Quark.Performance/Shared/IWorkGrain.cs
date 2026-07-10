using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.Shared;

/// <summary>
///     Shared grain contract reused by the MailboxContention, Fairness, SchedulingQuality, and
///     Backpressure runners (and AllocationBenchmarks), so each of those doesn't need its own
///     near-duplicate grain type.
/// </summary>
public interface IWorkGrain : IGrainWithStringKey
{
    /// <summary>Busy-spins for <paramref name="microseconds" /> then returns this activation's call count.</summary>
    ValueTask<long> DoWorkAsync(int microseconds);
}
