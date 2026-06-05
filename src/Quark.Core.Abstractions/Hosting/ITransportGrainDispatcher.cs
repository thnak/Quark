using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Dispatches an incoming transport message to the correct grain method,
///     mapping a numeric method ID and pre-serialized argument bytes to a strongly-typed
///     <see cref="IGrainCallInvoker" /> overload.
///     One implementation is generated per grain or observer interface by
///     <c>Quark.CodeGenerator</c>.
/// </summary>
public interface ITransportGrainDispatcher
{
    Task<object?> DispatchAsync(
        GrainId grainId,
        uint methodId,
        ReadOnlyMemory<byte> argumentPayload,
        IGrainCallInvoker invoker,
        CancellationToken cancellationToken = default);
}
