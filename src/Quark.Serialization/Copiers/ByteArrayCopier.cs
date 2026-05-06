using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Serialization.Copiers;

/// <summary>
/// Deep copier for <c>byte[]</c>. Arrays are mutable, so cloning is required for Orleans-style copy isolation.
/// </summary>
public sealed class ByteArrayCopier : IDeepCopier<byte[]?>
{
    /// <inheritdoc/>
    public byte[]? DeepCopy(byte[]? original, CopyContext context)
    {
        if (original is null)
            return null;

        var copy = new byte[original.Length];
        original.CopyTo(copy, 0);
        return copy;
    }
}
