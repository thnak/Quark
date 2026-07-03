using System.Collections.Concurrent;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Shell-owned child set.  Lives in the activation's memory bag; lost on deactivation.
///     Attach/Detach may race with the mailbox-ordered cascade read — ConcurrentDictionary
///     ensures correctness without blocking.
/// </summary>
internal sealed class ChildRegistry
{
    private readonly ConcurrentDictionary<GrainId, ChildTerminationMode> _children = new();

    public void Attach(GrainId child, ChildTerminationMode mode) => _children[child] = mode;

    public bool Detach(GrainId child) => _children.TryRemove(child, out _);

    public IReadOnlyCollection<GrainId> Snapshot(ChildTerminationMode mode)
    {
        var result = new List<GrainId>();
        foreach ((GrainId id, ChildTerminationMode value) in _children)
        {
            if (value == mode)
                result.Add(id);
        }
        return result;
    }

    public bool IsEmpty => _children.IsEmpty;
}
