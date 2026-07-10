using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Hand-written <see cref="IDeepCopier{T}" /> for <see cref="DurableDedupRecord" /> — required by
///     <c>IGrainStorage</c> providers (e.g. the in-memory provider) that snapshot state via
///     <see cref="ICopierProvider" /> rather than serializing it. See <see cref="DurableDedupRecordCodec" />
///     for why this is hand-written instead of generated.
/// </summary>
internal sealed class DurableDedupRecordCopier : IDeepCopier<DurableDedupRecord>
{
    public DurableDedupRecord DeepCopy(DurableDedupRecord input, CopyContext context)
    {
        if (input is null)
        {
            return default!;
        }

        DurableDedupRecord? existing = context.TryGetCopy<DurableDedupRecord>(input);
        if (existing is not null)
        {
            return existing;
        }

        var copy = new DurableDedupRecord();
        context.RecordCopy(input, copy);
        copy.ArgHash = input.ArgHash;
        copy.Payload = input.Payload is null ? null : (byte[])input.Payload.Clone();
        copy.CreatedAtUtcTicks = input.CreatedAtUtcTicks;
        return copy;
    }
}
