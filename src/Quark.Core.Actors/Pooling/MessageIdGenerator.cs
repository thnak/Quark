using System.Runtime.CompilerServices;

namespace Quark.Core.Actors.Pooling;

/// <summary>
///     High-performance message ID generator using incrementing counter.
///     Eliminates GUID allocation overhead in the messaging hot path.
/// </summary>
public static class MessageIdGenerator
{
    private static long _nextId;

    /// <summary>
    ///     Generates a unique message ID without allocating a GUID.
    ///     Uses an incrementing counter with atomic operations.
    /// </summary>
    /// <returns>A unique message ID string.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Generate()
    {
        // Increment atomically and format as string
        var id = Interlocked.Increment(ref _nextId);
        return id.ToString();
    }

    /// <summary>
    ///     Generates a unique message ID with a prefix.
    ///     Useful for debugging and tracing specific message types.
    /// </summary>
    /// <param name="prefix">The prefix to prepend to the ID.</param>
    /// <returns>A unique message ID string with the specified prefix.</returns>
    public static string GenerateWithPrefix(string prefix)
    {
        var id = Interlocked.Increment(ref _nextId);
        return $"{prefix}{id}";
    }
}
