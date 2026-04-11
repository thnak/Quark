namespace Quark.Core.Abstractions;

/// <summary>
/// Routes an outbound grain call from a grain proxy to the runtime dispatcher.
/// Implemented by the runtime and injected into generated proxy classes.
/// </summary>
public interface IGrainCallInvoker
{
    /// <summary>
    /// Invokes a grain method that returns <see cref="Task{TResult}"/>.
    /// </summary>
    /// <typeparam name="TResult">The return type of the grain method.</typeparam>
    /// <param name="grainId">Target grain identity.</param>
    /// <param name="methodId">Stable numeric method identifier (assigned by the codegen).</param>
    /// <param name="arguments">Serialized or boxed method arguments.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<TResult> InvokeAsync<TResult>(
        GrainId grainId,
        uint methodId,
        object?[]? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes a grain method that returns <see cref="Task"/> (void-like).
    /// </summary>
    Task InvokeVoidAsync(
        GrainId grainId,
        uint methodId,
        object?[]? arguments = null,
        CancellationToken cancellationToken = default);
}
