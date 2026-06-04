namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Dispatches method calls to a local observer object (which is not a <c>Grain</c> subclass)
///     by method ID.  One implementation is hand-written or generated per observer class.
/// </summary>
public interface IObserverMethodInvoker
{
    /// <summary>
    ///     Invokes the observer method identified by <paramref name="methodId" />.
    /// </summary>
    ValueTask<object?> Invoke(object target, uint methodId, object?[]? arguments);
}
