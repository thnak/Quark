using System.Collections.Concurrent;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     DI singleton test double recording the order in which grain-method invocations actually
///     started executing — observed from outside the grain, since a blocked call's own state
///     mutations haven't happened yet.
/// </summary>
public sealed class EntryLog
{
    private readonly ConcurrentQueue<int> _entries = new();

    public void Record(int id) => _entries.Enqueue(id);

    public int[] Snapshot() => _entries.ToArray();
}
