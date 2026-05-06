using Quark.Core.Abstractions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Quark.Runtime;

/// <summary>
/// Represents a single live grain activation on this silo.
/// Owns a sequential <see cref="Channel{T}"/> that ensures grain methods are
/// processed one at a time (single-threaded turn-based execution model).
/// </summary>
public sealed class GrainActivation : IAsyncDisposable
{
    private readonly ILogger<GrainActivation> _logger;

    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingLoop;

    /// <summary>The grain instance.</summary>
    public Grain Grain { get; }

    /// <summary>The activation context (identity + lifecycle).</summary>
    public GrainContext Context { get; }

    internal GrainActivation(Grain grain, GrainContext context, ILogger<GrainActivation> logger)
    {
        _logger = logger;
        Grain = grain;
        Context = context;
        _processingLoop = RunLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Posts a unit of work to this grain's sequential scheduler.
    /// The work item will be executed after all previously posted items complete.
    /// </summary>
    public async ValueTask PostAsync(Func<Task> workItem)
    {
        await _queue.Writer.WriteAsync(workItem, _cts.Token).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await foreach (Func<Task> work in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error executing grain method on {GrainId}", Context.GrainId);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        try
        {
            await _processingLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await _cts.CancelAsync();
        _cts.Dispose();
    }
}
