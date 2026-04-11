using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Lifecycle;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class LifecycleSubjectTests
{
    [Fact]
    public async Task StartAsync_CallsObservers_InAscendingStageOrder()
    {
        var subject = new LifecycleSubject();
        var order = new List<int>();

        subject.Subscribe("obs3", 300, new ActionObserver(
            start: async _ => { await Task.Yield(); order.Add(3); }));
        subject.Subscribe("obs1", 100, new ActionObserver(
            start: async _ => { await Task.Yield(); order.Add(1); }));
        subject.Subscribe("obs2", 200, new ActionObserver(
            start: async _ => { await Task.Yield(); order.Add(2); }));

        await subject.StartAsync();

        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public async Task StopAsync_CallsObservers_InDescendingStageOrder()
    {
        var subject = new LifecycleSubject();
        var order = new List<int>();

        subject.Subscribe("obs1", 100, new ActionObserver(
            stop: async _ => { await Task.Yield(); order.Add(1); }));
        subject.Subscribe("obs2", 200, new ActionObserver(
            stop: async _ => { await Task.Yield(); order.Add(2); }));
        subject.Subscribe("obs3", 300, new ActionObserver(
            stop: async _ => { await Task.Yield(); order.Add(3); }));

        await subject.StartAsync();
        await subject.StopAsync();

        Assert.Equal(new[] { 3, 2, 1 }, order);
    }

    [Fact]
    public async Task Unsubscribe_BeforeStart_ObserverNotCalled()
    {
        var subject = new LifecycleSubject();
        bool called = false;

        var sub = subject.Subscribe("obs", 100, new ActionObserver(
            start: _ => { called = true; return Task.CompletedTask; }));

        sub.Dispose();
        await subject.StartAsync();

        Assert.False(called);
    }

    [Fact]
    public async Task StartAsync_IsIdempotent_SecondCallDoesNothing()
    {
        var subject = new LifecycleSubject();
        int callCount = 0;

        subject.Subscribe("obs", 100, new ActionObserver(
            start: _ => { callCount++; return Task.CompletedTask; }));

        await subject.StartAsync();
        await subject.StartAsync();   // second call should be a no-op

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task StopAsync_PropagatesExceptions_FromAllObservers()
    {
        var subject = new LifecycleSubject();

        subject.Subscribe("bad1", 100, new ActionObserver(
            stop: _ => throw new InvalidOperationException("stop1")));
        subject.Subscribe("bad2", 200, new ActionObserver(
            stop: _ => throw new InvalidOperationException("stop2")));

        await subject.StartAsync();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => subject.StopAsync());
        Assert.True(ex is AggregateException or InvalidOperationException);
    }

    private sealed class ActionObserver(
        Func<CancellationToken, Task>? start = null,
        Func<CancellationToken, Task>? stop = null) : ILifecycleObserver
    {
        public Task OnStart(CancellationToken ct) =>
            start?.Invoke(ct) ?? Task.CompletedTask;
        public Task OnStop(CancellationToken ct) =>
            stop?.Invoke(ct) ?? Task.CompletedTask;
    }
}
