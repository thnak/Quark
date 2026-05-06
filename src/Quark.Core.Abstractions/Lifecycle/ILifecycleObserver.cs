namespace Quark.Core.Abstractions.Lifecycle;

/// <summary>
///     An observer that participates in a lifecycle at a specific stage.
/// </summary>
public interface ILifecycleObserver
{
    /// <summary>Called when the lifecycle is starting this stage.</summary>
    Task OnStart(CancellationToken cancellationToken = default);

    /// <summary>Called when the lifecycle is stopping this stage (in reverse order).</summary>
    Task OnStop(CancellationToken cancellationToken = default);
}
