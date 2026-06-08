using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Timers;
using Quark.Runtime;
using Quark.Streaming.Abstractions;
using Streaming.Simple.GrainInterfaces;

namespace Streaming.Simple.Grains;

public sealed class ProducerState
{
    public IAsyncStream<int>? Stream { get; set; }
    public IGrainTimer? Timer { get; set; }
    public int Counter { get; set; }
}

public sealed class ProducerBehavior : IGrainBehavior, IProducerGrain
{
    private readonly IActivationMemory<ProducerState> _memory;
    private readonly IActivationShellAccessor _shellAccessor;
    private readonly IStreamProvider? _streamProvider;

    public ProducerBehavior(
        IActivationMemory<ProducerState> memory,
        IActivationShellAccessor shellAccessor,
        [FromKeyedServices("simple")] IStreamProvider? streamProvider = null)
    {
        _memory = memory;
        _shellAccessor = shellAccessor;
        _streamProvider = streamProvider;
    }

    private ProducerState S => _memory.Value;

    public Task StartProducing(string ns, Guid key)
    {
        if (S.Timer is not null)
            throw new InvalidOperationException("Already producing.");

        if (_streamProvider is null)
            throw new InvalidOperationException("Stream provider not registered.");

        S.Stream = _streamProvider.GetStream<int>(StreamId.Create(ns, key));
        S.Timer = _shellAccessor.Shell.RegisterTimer<ProducerState>(
            static async (state, _) =>
            {
                state.Counter++;
                await state.Stream!.OnNextAsync(state.Counter);
            },
            S,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(1),
                Period = TimeSpan.FromSeconds(1)
            });

        return Task.CompletedTask;
    }

    public Task StopProducing()
    {
        S.Timer?.Dispose();
        S.Timer = null;
        S.Stream = null;
        return Task.CompletedTask;
    }
}
