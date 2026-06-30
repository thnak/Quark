using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Xunit;

namespace Quark.Tests.Unit.Integration;

// A proxy whose public surface returns ValueTask<T>/ValueTask directly — no .AsTask().
// This is a compile-time guard: if IGrainCallInvoker.InvokeAsync changes back to returning
// Task<T>, the assignments below will produce CS0029 and the file won't build.
file sealed class ValueTaskCounterProxy(GrainId grainId, IGrainCallInvoker invoker)
{
    public ValueTask<long> IncrementAsync()
        => invoker.InvokeAsync<CounterBehavior_IncrementInvokable, long>(grainId, new CounterBehavior_IncrementInvokable());

    public ValueTask<long> GetValueAsync()
        => invoker.InvokeAsync<CounterBehavior_GetValueInvokable, long>(grainId, new CounterBehavior_GetValueInvokable());

    public ValueTask ResetAsync()
        => invoker.InvokeVoidAsync(grainId, new CounterBehavior_ResetInvokable());
}

/// <summary>
/// Verifies that <see cref="IGrainCallInvoker.InvokeAsync{TInvokable,TResult}"/> and
/// <see cref="IGrainCallInvoker.InvokeVoidAsync{TInvokable}"/> return <see cref="ValueTask{TResult}"/>
/// and <see cref="ValueTask"/> respectively, and that callers can consume them without .AsTask().
/// </summary>
public sealed class ValueTaskInvokerTests : IAsyncLifetime
{
    private GrainCallFixture _fixture = null!;

    public Task InitializeAsync()
    {
        _fixture = new GrainCallFixture();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _fixture.DisposeAsync().AsTask();

    private static GrainId CounterGrainId(string key) => new(new GrainType("CounterGrain"), key);

    // ── InvokeAsync (typed result) ────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ReturnsValueTaskOfT_DirectAssignment()
    {
        var grainId = CounterGrainId("vt-direct");
        // Compiles only when InvokeAsync returns ValueTask<T>.
        ValueTask<long> vt = _fixture.Invoker.InvokeAsync<CounterBehavior_IncrementInvokable, long>(
            grainId, new CounterBehavior_IncrementInvokable());
        long result = await vt;
        Assert.Equal(1L, result);
    }

    [Fact]
    public async Task InvokeAsync_MultipleAwaits_AccumulatesState()
    {
        var grainId = CounterGrainId("vt-accum");
        long r1 = await _fixture.Invoker.InvokeAsync<CounterBehavior_IncrementInvokable, long>(
            grainId, new CounterBehavior_IncrementInvokable());
        long r2 = await _fixture.Invoker.InvokeAsync<CounterBehavior_IncrementInvokable, long>(
            grainId, new CounterBehavior_IncrementInvokable());
        long r3 = await _fixture.Invoker.InvokeAsync<CounterBehavior_IncrementInvokable, long>(
            grainId, new CounterBehavior_IncrementInvokable());
        Assert.Equal(1L, r1);
        Assert.Equal(2L, r2);
        Assert.Equal(3L, r3);
    }

    // ── InvokeVoidAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeVoidAsync_ReturnsValueTask_DirectAssignment()
    {
        var grainId = CounterGrainId("vt-void");
        await _fixture.Invoker.InvokeAsync<CounterBehavior_IncrementInvokable, long>(
            grainId, new CounterBehavior_IncrementInvokable());

        // Compiles only when InvokeVoidAsync returns ValueTask.
        ValueTask vt = _fixture.Invoker.InvokeVoidAsync(grainId, new CounterBehavior_ResetInvokable());
        await vt;

        long value = await _fixture.Invoker.InvokeAsync<CounterBehavior_GetValueInvokable, long>(
            grainId, new CounterBehavior_GetValueInvokable());
        Assert.Equal(0L, value);
    }

    // ── ValueTask-typed proxy end-to-end ─────────────────────────────────────

    [Fact]
    public async Task ValueTaskProxy_IncrementAndGet_ReturnsCorrectValues()
    {
        var proxy = new ValueTaskCounterProxy(CounterGrainId("vt-proxy-1"), _fixture.Invoker);
        long r1 = await proxy.IncrementAsync();
        long r2 = await proxy.IncrementAsync();
        long get = await proxy.GetValueAsync();
        Assert.Equal(1L, r1);
        Assert.Equal(2L, r2);
        Assert.Equal(2L, get);
    }

    [Fact]
    public async Task ValueTaskProxy_ResetAsync_ResetsToZero()
    {
        var proxy = new ValueTaskCounterProxy(CounterGrainId("vt-proxy-reset"), _fixture.Invoker);
        await proxy.IncrementAsync();
        await proxy.IncrementAsync();
        await proxy.ResetAsync();
        Assert.Equal(0L, await proxy.GetValueAsync());
    }

    [Fact]
    public async Task ValueTaskProxy_ConcurrentCalls_SerializedAndCorrect()
    {
        var proxy = new ValueTaskCounterProxy(CounterGrainId("vt-proxy-concurrent"), _fixture.Invoker);
        var tasks = Enumerable.Range(0, 10).Select(_ => proxy.IncrementAsync().AsTask()).ToList();
        long[] results = await Task.WhenAll(tasks);
        Assert.Equal(10L, await proxy.GetValueAsync());
        Assert.Equal(
            Enumerable.Range(1, 10).Select(i => (long)i).OrderBy(x => x),
            results.OrderBy(x => x));
    }
}
