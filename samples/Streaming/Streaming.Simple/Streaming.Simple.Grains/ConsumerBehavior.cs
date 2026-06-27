using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Streaming.Abstractions;
using Streaming.Simple.GrainInterfaces;

namespace Streaming.Simple.Grains;

public sealed class ConsumerBehavior : IGrainBehavior, IConsumerGrain
{
    private readonly IActivationMemory<ConsumerState> _memory;
    private readonly IStreamProvider? _streamProvider;
    private readonly ILogger<IConsumerGrain> _logger;

    public ConsumerBehavior(
        IActivationMemory<ConsumerState> memory,
        ILogger<IConsumerGrain> logger,
        [FromKeyedServices("simple")] IStreamProvider? streamProvider = null)
    {
        _memory = memory;
        _logger = logger;
        _streamProvider = streamProvider;
    }

    private ConsumerState S => _memory.Value;

    public async Task Subscribe(StreamId streamId)
    {
        if (_streamProvider is null)
        {
            return;
        }

        IAsyncStream<int> stream = _streamProvider.GetStream<int>(streamId);
        S.Handle = await stream.SubscribeAsync(new LoggingObserver(_logger));
    }

    public async Task Unsubscribe()
    {
        if (S.Handle is not null)
        {
            await S.Handle.UnsubscribeAsync();
            S.Handle = null;
        }
    }

    private sealed class LoggingObserver(ILogger logger) : IAsyncObserver<int>
    {
        public Task OnNextAsync(int item, StreamSequenceToken? token = null)
        {
            logger.LogInformation("[Consumer] Received: {Item}", item);
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            logger.LogError(ex, "[Consumer] Stream error");
            return Task.CompletedTask;
        }

        public Task OnCompletedAsync() => Task.CompletedTask;
    }
}
