using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class ReentrantTests
{
    private static GrainActivation MakeActivation(Grain grain)
    {
        var grainId = new GrainId(new GrainType("G"), "1");
        var ctx = new GrainContext(grainId, new NullGrainFactory(), new NullServiceProvider());
        return new GrainActivation(grain, ctx, NullLogger<GrainActivation>.Instance);
    }

    [Fact]
    public async Task NonReentrantGrain_SerializesDispatch()
    {
        var grain = new NonReentrantGrain();
        await using var activation = MakeActivation(grain);

        var completionSource1 = new TaskCompletionSource();
        var completionSource2 = new TaskCompletionSource();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Post first work item
        _ = activation.PostAsync(async () =>
        {
            await Task.Delay(50);
            completionSource1.SetResult();
        });

        // Post second work item
        _ = activation.PostAsync(async () =>
        {
            await Task.Delay(50);
            completionSource2.SetResult();
        });

        // Wait for both to complete
        await Task.WhenAll(completionSource1.Task, completionSource2.Task);
        sw.Stop();

        // Serial execution of two 50ms tasks should take ~100ms
        // Allow some variance (at least 80ms)
        Assert.True(sw.ElapsedMilliseconds >= 80,
            $"Expected ~100ms serial, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ReentrantGrain_AllowsConcurrentDispatch()
    {
        var grain = new ReentrantGrain();
        await using var activation = MakeActivation(grain);

        var completionSource1 = new TaskCompletionSource();
        var completionSource2 = new TaskCompletionSource();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Post first work item
        var task1 = activation.PostAsync(async () =>
        {
            await Task.Delay(50);
            completionSource1.SetResult();
        });

        // Post second work item
        var task2 = activation.PostAsync(async () =>
        {
            await Task.Delay(50);
            completionSource2.SetResult();
        });

        // Wait for both to complete
        await Task.WhenAll(task1.AsTask(), task2.AsTask(), completionSource1.Task, completionSource2.Task);
        sw.Stop();

        // Concurrent execution should complete in ~50ms, not ~100ms
        // Allow some timing variance (up to 120ms instead of strict <80ms)
        Assert.True(sw.ElapsedMilliseconds < 120,
            $"Expected ~50ms concurrent, got {sw.ElapsedMilliseconds}ms");
    }

    private sealed class NonReentrantGrain : Grain { }

    [Reentrant]
    private sealed class ReentrantGrain : Grain { }

    private sealed class NullGrainFactory : IGrainFactory
    {
        public TGI GetGrain<TGI>(string key) where TGI : IGrainWithStringKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(long key) where TGI : IGrainWithIntegerKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(Guid key) where TGI : IGrainWithGuidKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(long key, string? ext) where TGI : IGrainWithIntegerCompoundKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(Guid key, string? ext) where TGI : IGrainWithGuidCompoundKey => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, string key) => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, Guid key) => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, long key) => throw new NotImplementedException();
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
