using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Unit.Integration;

// ---------------------------------------------------------------------------
// Resource, grain interface, behavior
// ---------------------------------------------------------------------------

public sealed class ManagedBuffer
{
    public int InitCount { get; set; }
    public int DestroyCount { get; set; }
    public string Data { get; set; } = string.Empty;
}

public interface IManagedBufferGrain : IGrainWithStringKey
{
    Task<long> GetInitCountAsync();
    Task<string> GetDataAsync();
    Task SetDataAsync(string value);
    Task SelfDestructAsync();
}

public sealed class ManagedBufferBehavior : IGrainBehavior, IManagedBufferGrain, IActivationLifecycle
{
    private readonly IManagedActivationMemory<ManagedBuffer> _managed;
    private readonly IActivationShellAccessor _shell;

    public ManagedBufferBehavior(
        IManagedActivationMemory<ManagedBuffer> managed,
        IActivationShellAccessor shell)
    {
        _shell = shell;
        _managed = managed
            .Init(() =>
            {
                var buf = new ManagedBuffer();
                buf.InitCount++;
                return Task.FromResult(buf);
            })
            .Destroy(b => { b.DestroyCount++; return Task.CompletedTask; });
    }

    public async Task<long> GetInitCountAsync()
    {
        var buf = await _managed.GetAsync();
        return buf.InitCount;
    }

    public async Task<string> GetDataAsync()
    {
        var buf = await _managed.GetAsync();
        return buf.Data;
    }

    public async Task SetDataAsync(string value)
    {
        var buf = await _managed.GetAsync();
        buf.Data = value;
    }

    public Task SelfDestructAsync()
    {
        _shell.Shell.Deactivate(DeactivationReason.ApplicationRequested);
        return Task.CompletedTask;
    }

    public Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Invokables (normally generated)
// ---------------------------------------------------------------------------

internal readonly struct ManagedBuffer_GetInitCountInvokable : IGrainInvokable<long>
{
    public uint MethodId => 0u;
    public ValueTask<long> Invoke(IGrainBehavior behavior) => new(((IManagedBufferGrain)behavior).GetInitCountAsync());
    public void Serialize(ref CodecWriter writer) { }
    public long DeserializeResult(ref CodecReader reader) => reader.ReadInt64();
}

internal readonly struct ManagedBuffer_GetDataInvokable : IGrainInvokable<string>
{
    public uint MethodId => 1u;
    public ValueTask<string> Invoke(IGrainBehavior behavior) => new(((IManagedBufferGrain)behavior).GetDataAsync());
    public void Serialize(ref CodecWriter writer) { }
    public string DeserializeResult(ref CodecReader reader) => reader.ReadString();
}

internal readonly struct ManagedBuffer_SetDataInvokable(string value) : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((IManagedBufferGrain)behavior).SetDataAsync(value));
    public void Serialize(ref CodecWriter writer) => writer.WriteString(value);
}

internal readonly struct ManagedBuffer_SelfDestructInvokable : IGrainVoidInvokable
{
    public uint MethodId => 3u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((IManagedBufferGrain)behavior).SelfDestructAsync());
    public void Serialize(ref CodecWriter writer) { }
}

// ---------------------------------------------------------------------------
// Proxy (normally generated)
// ---------------------------------------------------------------------------

public sealed class ManagedBufferGrainProxy : IManagedBufferGrain
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public ManagedBufferGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public Task<long> GetInitCountAsync()
        => _invoker.InvokeAsync<ManagedBuffer_GetInitCountInvokable, long>(_grainId, new ManagedBuffer_GetInitCountInvokable());

    public Task<string> GetDataAsync()
        => _invoker.InvokeAsync<ManagedBuffer_GetDataInvokable, string>(_grainId, new ManagedBuffer_GetDataInvokable());

    public Task SetDataAsync(string value)
        => _invoker.InvokeVoidAsync(_grainId, new ManagedBuffer_SetDataInvokable(value));

    public Task SelfDestructAsync()
        => _invoker.InvokeVoidAsync(_grainId, new ManagedBuffer_SelfDestructInvokable());
}

// ---------------------------------------------------------------------------
// Fixture
// ---------------------------------------------------------------------------

public sealed class ManagedMemoryFixture : IAsyncDisposable
{
    private readonly GrainActivationTable _activationTable;
    private readonly ServiceProvider _serviceProvider;

    public ManagedMemoryFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "managed-memory";
            o.SiloName = "silo0";
        });

        services.AddSingleton<LifecycleSubject>();
        services.AddSingleton<GrainTypeRegistry>();
        services.AddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());
        services.AddSingleton<InMemoryGrainDirectory>();
        services.AddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());
        services.AddSingleton<GrainActivationTable>();

        services.AddScoped<ActivationShellAccessor>();
        services.AddScoped<IActivationShellAccessor>(sp => sp.GetRequiredService<ActivationShellAccessor>());
        services.AddScoped<CallContext>();
        services.AddScoped<ICallContext>(sp => sp.GetRequiredService<CallContext>());
        services.AddScoped<ICallContextSetter>(sp => sp.GetRequiredService<CallContext>());
        services.AddScoped<IBehaviorResolver, BehaviorResolver>();

        services.AddManagedActivationMemory<ManagedBuffer>();
        services.AddTransient<ManagedBufferBehavior>();

        services.AddSingleton<GrainProxyFactoryRegistry>();
        services.AddSingleton<GrainInterfaceTypeRegistry>();

        LocalGrainFactory? grainFactoryRef = null;
        services.AddSingleton<IGrainFactory>(_ =>
            grainFactoryRef ?? throw new InvalidOperationException("Not yet wired."));

        _serviceProvider = services.BuildServiceProvider();

        GrainTypeRegistry typeRegistry = _serviceProvider.GetRequiredService<GrainTypeRegistry>();
        typeRegistry.Register(new GrainType("ManagedBufferGrain"), typeof(ManagedBufferBehavior));

        GrainProxyFactoryRegistry proxyRegistry = _serviceProvider.GetRequiredService<GrainProxyFactoryRegistry>();
        GrainInterfaceTypeRegistry interfaceRegistry = _serviceProvider.GetRequiredService<GrainInterfaceTypeRegistry>();
        interfaceRegistry.Register(typeof(IManagedBufferGrain), new GrainType("ManagedBufferGrain"));
        proxyRegistry.Register<IManagedBufferGrain, ManagedBufferGrainProxy>((grainId, invoker) =>
            new ManagedBufferGrainProxy(grainId, invoker));

        _activationTable = _serviceProvider.GetRequiredService<GrainActivationTable>();
        IGrainDirectory directory = _serviceProvider.GetRequiredService<IGrainDirectory>();
        IOptions<SiloRuntimeOptions> siloOptions = _serviceProvider.GetRequiredService<IOptions<SiloRuntimeOptions>>();

        var callInvoker = new LocalGrainCallInvoker(
            _activationTable, typeRegistry, directory,
            _serviceProvider, siloOptions,
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance);

        grainFactoryRef = new LocalGrainFactory(proxyRegistry, interfaceRegistry, callInvoker);
        Client = new LocalClusterClient(grainFactoryRef);
    }

    public IClusterClient Client { get; }
    public GrainActivationTable ActivationTable => _activationTable;

    public async ValueTask DisposeAsync()
    {
        await _activationTable.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class ManagedMemoryGrainTests : IAsyncLifetime
{
    private ManagedMemoryFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new ManagedMemoryFixture();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _fixture.DisposeAsync().AsTask();

    private IManagedBufferGrain GetGrain(string key)
        => _fixture.Client.GetGrain<IManagedBufferGrain>(key);

    [Fact]
    public async Task Factory_Called_On_First_Access()
    {
        IManagedBufferGrain grain = GetGrain("init-count");
        long count = await grain.GetInitCountAsync();
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task Factory_Called_Exactly_Once_Per_Activation()
    {
        IManagedBufferGrain grain = GetGrain("factory-once");
        await grain.GetInitCountAsync();
        await grain.GetInitCountAsync();
        long count = await grain.GetInitCountAsync();
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task Resource_Persists_Across_Calls()
    {
        IManagedBufferGrain grain = GetGrain("persist-data");
        await grain.SetDataAsync("hello");
        string data = await grain.GetDataAsync();
        Assert.Equal("hello", data);
    }

    [Fact]
    public async Task Deactivation_Invokes_Destroy_Callback()
    {
        IManagedBufferGrain grain = GetGrain("destroy-test");
        await grain.GetDataAsync(); // trigger init

        var grainId = new GrainId(new GrainType("ManagedBufferGrain"), "destroy-test");
        _fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation);
        ManagedActivationMemoryHolder<ManagedBuffer> holder = activation!.GetOrCreateManagedHolder<ManagedBuffer>();

        await grain.SelfDestructAsync();
        await Task.Delay(200);

        // The destroy callback increments DestroyCount on the buffer.
        ManagedBuffer buf = await holder.GetAsync();
        Assert.Equal(1, buf.DestroyCount);
    }

    [Fact]
    public async Task Deactivation_Does_Not_Throw_When_Resource_Never_Accessed()
    {
        IManagedBufferGrain grain = GetGrain("no-access-test");
        // Force activation by making a grain call that doesn't touch managed memory directly.
        // SelfDestruct triggers deactivation; the holder was never initialized.
        await grain.SelfDestructAsync();
        await Task.Delay(200);
        // No exception expected — idle holders skip cleanup.
    }
}
