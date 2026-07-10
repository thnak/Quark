using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>
///     Optional snapshot store for <see cref="JournaledGrain{TState,TEvent}" />. A snapshot is a
///     replay-shortcut only; the event log remains the source of truth. A missing snapshot is
///     normal (activation full-replays). A present-but-corrupt snapshot must surface as a
///     <see cref="CorruptSnapshotException" /> rather than silently producing wrong state.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>
    ///     Reads the latest snapshot for <paramref name="grainId" />, or <c>null</c> if none exists.
    ///     Durable providers throw <see cref="CorruptSnapshotException" /> when a stored snapshot
    ///     cannot be deserialized into <typeparamref name="TState" />.
    /// </summary>
    Task<SnapshotEnvelope<TState>?> ReadSnapshotAsync<TState>(
        GrainId grainId, CancellationToken ct = default) where TState : class;

    /// <summary>Writes (replaces) the snapshot for <paramref name="grainId" />.</summary>
    Task WriteSnapshotAsync<TState>(
        GrainId grainId, SnapshotEnvelope<TState> snapshot, CancellationToken ct = default)
        where TState : class;

    /// <summary>Deletes any stored snapshot for <paramref name="grainId" /> (recovery path).</summary>
    Task ClearSnapshotAsync(GrainId grainId, CancellationToken ct = default);
}

/// <summary>A point-in-time projection of grain state and the log version it folds up to.</summary>
public sealed class SnapshotEnvelope<TState>
{
    public SnapshotEnvelope(int version, TState state)
    {
        Version = version;
        State = state;
    }

    /// <summary>Number of events folded into <see cref="State" /> — i.e. the index of the next event.</summary>
    public int Version { get; }

    /// <summary>State after applying events <c>[0, Version)</c>.</summary>
    public TState State { get; }
}

/// <summary>Thrown when a present snapshot is unusable (undeserializable or inconsistent with the log).</summary>
public sealed class CorruptSnapshotException : Exception
{
    public CorruptSnapshotException(GrainId grainId, int snapshotVersion, string message, Exception? inner = null)
        : base(message, inner)
    {
        GrainId = grainId;
        SnapshotVersion = snapshotVersion;
    }

    /// <summary>The grain whose snapshot is corrupt.</summary>
    public GrainId GrainId { get; }

    /// <summary>The version stamped on the offending snapshot.</summary>
    public int SnapshotVersion { get; }
}
