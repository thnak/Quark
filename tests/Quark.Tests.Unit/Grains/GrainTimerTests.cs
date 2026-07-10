using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Timers;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class GrainTimerTests
{
    private static GrainActivation MakeActivation(IServiceProvider? services = null)
    {
        var id = new GrainId(new GrainType("TimerTest"), "1");
        return new GrainActivation(id, id.Type, isReentrant: false,
            services ?? new NullServiceProvider(), NullLogger<GrainActivation>.Instance,
            SimpleActivationScheduler.Instance);
    }

    [Fact]
    public async Task Timer_FiresAfterDueTime()
    {
        await using var activation = MakeActivation();
        var counter = new Counter();
        _ = activation.RegisterTimer(
            static (c, _) => { Interlocked.Increment(ref c.Value); return Task.CompletedTask; },
            counter,
            new GrainTimerCreationOptions { DueTime = TimeSpan.FromMilliseconds(30), Period = Timeout.InfiniteTimeSpan });
        await Task.Delay(200);
        Assert.True(counter.Value >= 1, $"Expected >=1 fire, got {counter.Value}");
    }

    [Fact]
    public async Task Timer_FiresRepeatedly()
    {
        await using var activation = MakeActivation();
        var counter = new Counter();
        _ = activation.RegisterTimer(
            static (c, _) => { Interlocked.Increment(ref c.Value); return Task.CompletedTask; },
            counter,
            new GrainTimerCreationOptions { DueTime = TimeSpan.FromMilliseconds(20), Period = TimeSpan.FromMilliseconds(30) });
        await Task.Delay(300);
        Assert.True(counter.Value >= 3, $"Expected >=3 fires, got {counter.Value}");
    }

    [Fact]
    public async Task Timer_StopsWhenDisposed()
    {
        await using var activation = MakeActivation();
        var counter = new Counter();
        IGrainTimer timer = activation.RegisterTimer(
            static (c, _) => { Interlocked.Increment(ref c.Value); return Task.CompletedTask; },
            counter,
            new GrainTimerCreationOptions { DueTime = TimeSpan.FromMilliseconds(20), Period = TimeSpan.FromMilliseconds(20) });
        await Task.Delay(150);
        int countAtDispose = counter.Value;
        timer.Dispose();
        await Task.Delay(150);
        Assert.Equal(countAtDispose, counter.Value);
    }

    [Fact]
    public async Task Timer_DisposedOnGrainDeactivation()
    {
        var activation = MakeActivation();
        var counter = new Counter();
        _ = activation.RegisterTimer(
            static (c, _) => { Interlocked.Increment(ref c.Value); return Task.CompletedTask; },
            counter,
            new GrainTimerCreationOptions { DueTime = TimeSpan.FromMilliseconds(20), Period = TimeSpan.FromMilliseconds(20) });
        await Task.Delay(100);
        await activation.DisposeAsync();
        int countAfterDeactivate = counter.Value;
        await Task.Delay(150);
        Assert.Equal(countAfterDeactivate, counter.Value);
    }

    [Fact]
    public async Task Timer_UsesExplicitTimeProviderOverride_IgnoringRealClock()
    {
        var fakeTime = new FakeTimeProvider();
        await using var activation = MakeActivation();
        var counter = new Counter();
        _ = activation.RegisterTimer(
            static (c, _) => { Interlocked.Increment(ref c.Value); return Task.CompletedTask; },
            counter,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMinutes(5),
                Period = Timeout.InfiniteTimeSpan,
                TimeProvider = fakeTime,
            });

        // Real time passing must not fire a timer pinned to a fake clock.
        await Task.Delay(50);
        Assert.Equal(0, counter.Value);

        fakeTime.Advance(TimeSpan.FromMinutes(5));
        await WaitUntilAsync(() => counter.Value >= 1);
        Assert.Equal(1, counter.Value);
    }

    [Fact]
    public async Task Timer_ResolvesTimeProviderFromServiceProvider_WhenOptionsDoNotOverride()
    {
        var fakeTime = new FakeTimeProvider();
        var services = new ServiceCollection().AddSingleton<TimeProvider>(fakeTime).BuildServiceProvider();
        await using var activation = MakeActivation(services);
        var counter = new Counter();
        _ = activation.RegisterTimer(
            static (c, _) => { Interlocked.Increment(ref c.Value); return Task.CompletedTask; },
            counter,
            new GrainTimerCreationOptions { DueTime = TimeSpan.FromMinutes(5), Period = Timeout.InfiniteTimeSpan });

        await Task.Delay(50);
        Assert.Equal(0, counter.Value);

        fakeTime.Advance(TimeSpan.FromMinutes(5));
        await WaitUntilAsync(() => counter.Value >= 1);
        Assert.Equal(1, counter.Value);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(5);
        }
    }

    private sealed class Counter { public int Value; }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
