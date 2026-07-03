namespace Quark.Runtime;

internal interface IActivationScheduler : IAsyncDisposable
{
    ValueTask ScheduleAsync(GrainActivation activation, CancellationToken cancellationToken = default);
}
