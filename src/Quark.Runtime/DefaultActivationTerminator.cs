using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Diagnostics.Abstractions;
using Quark.Runtime.Clustering;

namespace Quark.Runtime;

/// <summary>
///     Default <see cref="IActivationTerminator"/> implementation.
///     Local leg: looks up the target in the local activation table and calls
///     <see cref="GrainActivation.Deactivate"/>.
///     Remote leg: looks up the owning silo via the directory, then sends a one-way
///     <c>TerminateRequest</c> frame through the peer connection.  Both legs are best-effort
///     and fire-and-forget — this method never throws and never awaits a child response.
/// </summary>
public sealed class DefaultActivationTerminator : IActivationTerminator
{
    private readonly GrainActivationTable _table;
    private readonly IGrainDirectory? _directory;
    private readonly ISiloRouter? _siloRouter;
    private readonly IQuarkDiagnosticListener? _diagnostics;

    public DefaultActivationTerminator(
        GrainActivationTable table,
        IGrainDirectory? directory = null,
        ISiloRouter? siloRouter = null,
        IQuarkDiagnosticListener? diagnostics = null)
    {
        _table = table;
        _directory = directory;
        _siloRouter = siloRouter;
        _diagnostics = diagnostics;
    }

    public void Terminate(GrainId target, DeactivationReason reason)
    {
        if (_table.TryGetActivation(target, out GrainActivation? activation))
        {
            activation!.Deactivate(reason);
            return;
        }

        if (_directory is null || _siloRouter is null)
            return;

        if (!_directory.TryLookup(target, out SiloAddress owner))
            return;

        if (!_siloRouter.TryGetInvoker(owner, out IGrainCallInvoker? invoker))
            return;

        if (invoker is not SiloCallInvoker siloInvoker)
            return;

        _ = siloInvoker.SendTerminateRequestAsync(target, 0, CancellationToken.None)
            .ContinueWith(
                t => _diagnostics?.OnChildTerminationFailed(
                    new ChildTerminationFailedEvent(target, t.Exception?.Flatten().InnerException)),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }
}
