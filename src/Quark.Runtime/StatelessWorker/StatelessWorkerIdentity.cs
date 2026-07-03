using System.Diagnostics.CodeAnalysis;

namespace Quark.Runtime.StatelessWorker;

/// <summary>
///     Helpers for encoding and decoding synthetic stateless-worker activation identities.
///     A worker id is <c>(Type, logicalKey + SENTINEL + ordinal)</c> where SENTINEL is
///     ASCII Unit Separator (0x1F), a character that Quark reserves in grain keys for this purpose.
///     User-supplied grain keys must not contain this character for stateless-worker grain types.
/// </summary>
internal static class StatelessWorkerIdentity
{
    // ASCII Unit Separator — reserved in Quark grain keys for synthetic worker identity encoding.
    internal const char Sentinel = '\x1F';

    /// <summary>
    ///     Encodes a logical grain id and a worker ordinal into a synthetic worker activation id.
    /// </summary>
    public static GrainId Encode(GrainId logicalId, int ordinal)
        => new(logicalId.Type,
            string.Concat(logicalId.Key.AsSpan(), Sentinel.ToString().AsSpan(),
                ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    ///     Decodes a synthetic worker id back to its logical grain id and ordinal.
    ///     Returns <c>false</c> when <paramref name="workerId"/> is not a valid synthetic id
    ///     (no SENTINEL in key or unparseable ordinal).
    /// </summary>
    public static bool TryDecode(
        GrainId workerId,
        [NotNullWhen(true)] out GrainId logicalId,
        out int ordinal)
    {
        int sentinelPos = workerId.Key.LastIndexOf(Sentinel);
        if (sentinelPos < 0)
        {
            logicalId = default;
            ordinal = 0;
            return false;
        }

        ReadOnlySpan<char> ordinalSpan = workerId.Key.AsSpan(sentinelPos + 1);
        if (!int.TryParse(ordinalSpan, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out ordinal))
        {
            logicalId = default;
            return false;
        }

        string logicalKey = workerId.Key[..sentinelPos];
        logicalId = new GrainId(workerId.Type, logicalKey);
        return true;
    }
}
