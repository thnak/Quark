using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Streaming.Abstractions;
using Quark.Streaming.InMemory;
using Quark.Testing.Harness;
using Xunit;

namespace Quark.Tests.Integration;

[Trait("category", "integration")]
public sealed class StreamingIntegrationTests
{
    // Direct unit-level tests (no cluster required)

    [Fact]
    public async Task ExplicitSubscription_ReceivesPublishedItems()
    {
        var provider = new InMemoryStreamProvider("chat", new StreamSubscriptionRegistry());
        var stream = provider.GetStream<string>(StreamId.Create("room", "general"));

        var received = new List<string>();
        await stream.SubscribeAsync((msg, _) => { received.Add(msg); return ValueTask.CompletedTask; });

        await stream.OnNextAsync("hello");
        await stream.OnNextAsync("world");

        Assert.Equal(["hello", "world"], received);
    }

    [Fact]
    public async Task UnsubscribeHandle_StopsDelivery()
    {
        var provider = new InMemoryStreamProvider("chat", new StreamSubscriptionRegistry());
        var stream = provider.GetStream<string>(StreamId.Create("room", "unsub"));

        var received = new List<string>();
        var handle = await stream.SubscribeAsync((msg, _) => { received.Add(msg); return ValueTask.CompletedTask; });

        await stream.OnNextAsync("before");
        await handle.UnsubscribeAsync();
        await stream.OnNextAsync("after");

        Assert.Equal(["before"], received);
    }

    [Fact]
    public async Task MultipleSubscribers_AllReceiveMessages()
    {
        var provider = new InMemoryStreamProvider("chat", new StreamSubscriptionRegistry());
        var stream = provider.GetStream<int>(StreamId.Create("nums", "test"));

        var a = new List<int>();
        var b = new List<int>();
        await stream.SubscribeAsync((n, _) => { a.Add(n); return ValueTask.CompletedTask; });
        await stream.SubscribeAsync((n, _) => { b.Add(n); return ValueTask.CompletedTask; });

        await stream.OnNextAsync(1);
        await stream.OnNextAsync(2);

        Assert.Equal([1, 2], a);
        Assert.Equal([1, 2], b);
    }

    // Grain self-subscription via TestCluster

    [Fact]
    public async Task GrainSubscribesInOnActivate_ReceivesStreamMessages()
    {
        await using var cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.AddMemoryStreams("events");
                services.AddGrainBehavior<IStreamListenerGrain, StreamListenerGrainBehavior>();
                services.AddScoped<IActivationMemory<StreamListenerState>>(sp =>
                    new ActivationMemoryAccessor<StreamListenerState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<StreamListenerState>()));
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IStreamListenerGrain, StreamListenerGrainProxy>();
            };
        });

        IStreamListenerGrain grain = cluster.Client.GetGrain<IStreamListenerGrain>("sensor1");
        await grain.GetLastValueAsync(); // triggers activation → OnActivateAsync → self-subscribe

        var provider = cluster.PrimarySilo.Services.GetRequiredKeyedService<IStreamProvider>("events");
        var stream = provider.GetStream<int>(StreamId.Create("readings", "sensor1"));
        await stream.OnNextAsync(42);
        await stream.OnNextAsync(99);

        // Poll until the grain reflects the last value (up to 2s)
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && await grain.GetLastValueAsync() != 99)
            await Task.Delay(10);

        Assert.Equal(99, await grain.GetLastValueAsync());
    }

    // Implicit subscription test — no manual Subscribe call; publish triggers auto-activation

    [Fact]
    public async Task ImplicitSubscription_AutoActivatesGrainAndDeliversFirstItem()
    {
        await using var cluster = await TestCluster.CreateAsync(options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.AddMemoryStreams("telemetry");
                services.AddImplicitStreamSubscription("sensor-ns", "ImplicitListenerGrain");
                services.AddGrainBehavior<IImplicitListenerGrain, ImplicitListenerGrainBehavior>();
                services.AddScoped<IActivationMemory<ImplicitListenerState>>(sp =>
                    new ActivationMemoryAccessor<ImplicitListenerState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<ImplicitListenerState>()));
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IImplicitListenerGrain, ImplicitListenerGrainProxy>();
            };
        });

        var provider = cluster.PrimarySilo.Services.GetRequiredKeyedService<IStreamProvider>("telemetry");
        var stream = provider.GetStream<int>(StreamId.Create("sensor-ns", "dev1"));

        // No prior grain activation — publish must auto-activate and deliver atomically.
        await stream.OnNextAsync(77);

        // Grain is already activated; value was stored during the publish call.
        IImplicitListenerGrain grain = cluster.Client.GetGrain<IImplicitListenerGrain>("dev1");
        Assert.Equal(77, await grain.GetReceivedAsync());
    }

    // Grain stub

    public interface IStreamListenerGrain : IGrainWithStringKey
    {
        Task<int> GetLastValueAsync();
    }

    private sealed class StreamListenerState
    {
        public int Last { get; set; }
    }

    [ImplicitStreamSubscription("readings")]
    private sealed class StreamListenerGrainBehavior : IGrainBehavior, IStreamListenerGrain,
        IAsyncObserver<int>, IActivationLifecycle
    {
        private readonly IActivationMemory<StreamListenerState> _memory;
        private readonly ICallContext _ctx;
        private readonly IServiceProvider _serviceProvider;

        public StreamListenerGrainBehavior(
            IActivationMemory<StreamListenerState> memory,
            ICallContext ctx,
            IServiceProvider serviceProvider)
        {
            _memory = memory;
            _ctx = ctx;
            _serviceProvider = serviceProvider;
        }

        public async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var provider = _serviceProvider.GetRequiredKeyedService<IStreamProvider>("events");
            var streamId = StreamId.Create("readings", _ctx.GrainId.Key);
            await provider.GetStream<int>(streamId).SubscribeAsync(this);
        }

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

        public Task<int> GetLastValueAsync() => Task.FromResult(_memory.Value.Last);
        public ValueTask OnNextAsync(int item, StreamSequenceToken? token = null) { _memory.Value.Last = item; return ValueTask.CompletedTask; }
        public ValueTask OnErrorAsync(Exception ex) => ValueTask.CompletedTask;
        public ValueTask OnCompletedAsync() => ValueTask.CompletedTask;
    }

    private readonly struct StreamListenerGrain_GetLastValueInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 0u;
        public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((IStreamListenerGrain)behavior).GetLastValueAsync());
        public void Serialize(ref CodecWriter writer) { }
        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
    }

    private sealed class StreamListenerGrainProxy : IStreamListenerGrain, IGrainProxyActivator<StreamListenerGrainProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public StreamListenerGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }

        public static StreamListenerGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task<int> GetLastValueAsync()
            => _invoker.InvokeAsync<StreamListenerGrain_GetLastValueInvokable, int>(
                _grainId, new StreamListenerGrain_GetLastValueInvokable()).AsTask();
    }

    // Implicit subscription grain stubs

    public interface IImplicitListenerGrain : IGrainWithStringKey
    {
        Task<int> GetReceivedAsync();
    }

    private sealed class ImplicitListenerState
    {
        public int Last { get; set; }
    }

    [ImplicitStreamSubscription("sensor-ns")]
    private sealed class ImplicitListenerGrainBehavior : IGrainBehavior, IImplicitListenerGrain,
        IAsyncObserver<int>, IActivationLifecycle
    {
        private readonly IActivationMemory<ImplicitListenerState> _memory;
        private readonly ICallContext _ctx;
        private readonly IServiceProvider _sp;

        public ImplicitListenerGrainBehavior(
            IActivationMemory<ImplicitListenerState> memory,
            ICallContext ctx,
            IServiceProvider sp)
        {
            _memory = memory;
            _ctx = ctx;
            _sp = sp;
        }

        public async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var provider = _sp.GetRequiredKeyedService<IStreamProvider>("telemetry");
            var streamId = StreamId.Create("sensor-ns", _ctx.GrainId.Key);
            await provider.GetStream<int>(streamId).SubscribeAsync(this);
        }

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

        public Task<int> GetReceivedAsync() => Task.FromResult(_memory.Value.Last);

        public ValueTask OnNextAsync(int item, StreamSequenceToken? token = null)
        {
            _memory.Value.Last = item;
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception ex) => ValueTask.CompletedTask;
        public ValueTask OnCompletedAsync() => ValueTask.CompletedTask;
    }

    private readonly struct ImplicitListenerGrain_GetReceivedInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 0u;
        public ValueTask<int> Invoke(IGrainBehavior behavior) => new(((IImplicitListenerGrain)behavior).GetReceivedAsync());
        public void Serialize(ref CodecWriter writer) { }
        public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
    }

    private sealed class ImplicitListenerGrainProxy : IImplicitListenerGrain, IGrainProxyActivator<ImplicitListenerGrainProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public ImplicitListenerGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }

        public static ImplicitListenerGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task<int> GetReceivedAsync()
            => _invoker.InvokeAsync<ImplicitListenerGrain_GetReceivedInvokable, int>(
                _grainId, new ImplicitListenerGrain_GetReceivedInvokable()).AsTask();
    }
}
