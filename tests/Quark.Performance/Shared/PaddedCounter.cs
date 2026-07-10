using System.Runtime.InteropServices;

namespace Quark.Performance.Shared;

/// <summary>
///     Cache-line-padded (64 bytes) per-worker counter. One slot per worker, never touched by
///     another thread, so adjacent workers' counters in the same array can't false-share a cache
///     line. Generalizes the private <c>PaddedCounter</c> struct already used by
///     <c>PingPong/PingPongRunner.cs</c> for reuse across the newer runners; that file is left
///     untouched (its own copy stays private/unchanged).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct PaddedCounter
{
    [FieldOffset(0)]
    public long Value;
}
