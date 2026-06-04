using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Dispatches an incoming transport message to the correct grain method,
///     mapping a numeric method ID and raw argument array to a strongly-typed
///     <see cref="IGrainCallInvoker" /> overload.
///     One implementation is generated per grain or observer interface by
///     <c>Quark.CodeGenerator</c>.
/// </summary>
public interface ITransportGrainDispatcher
{
    Task<object?> DispatchAsync(
        GrainId grainId,
        uint methodId,
        object?[]? args,
        IGrainCallInvoker invoker,
        CancellationToken cancellationToken = default);
}
