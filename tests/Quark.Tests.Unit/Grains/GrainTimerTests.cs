using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Timers;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class GrainTimerTests
{
    private static async Task<(TimerTestGrain grain, GrainContext ctx, GrainActivation activation)> SetupAsync()
    {
        var grain = new TimerTestGrain();
        var id = new GrainId(new GrainType("TimerTestGrain"), "1");
        var ctx = new GrainContext(id, new NullGrainFactory(), new NullServiceProvider());
        var activation = new GrainActivation(grain, ctx, NullLogger<GrainActivation>.Instance);
        await ctx.ActivateAsync(grain);
        return (grain, ctx, activation);
    }

    [Fact]
    public async Task Timer_FiresAfterDueTime()
    {
        var (grain, _, activation) = await SetupAsync();
        grain.StartTimer(dueTime: TimeSpan.FromMilliseconds(30), period: Timeout.InfiniteTimeSpan);
        await Task.Delay(200);
        Assert.True(grain.FireCount >= 1, $"Expected >=1 fire, got {grain.FireCount}");
        await activation.DisposeAsync();
    }

    [Fact]
    public async Task Timer_FiresRepeatedly()
    {
        var (grain, _, activation) = await SetupAsync();
        grain.StartTimer(dueTime: TimeSpan.FromMilliseconds(20), period: TimeSpan.FromMilliseconds(30));
        await Task.Delay(300);
        Assert.True(grain.FireCount >= 3, $"Expected >=3 fires, got {grain.FireCount}");
        await activation.DisposeAsync();
    }

    [Fact]
    public async Task Timer_StopsWhenDisposed()
    {
        var (grain, _, activation) = await SetupAsync();
        grain.StartTimer(dueTime: TimeSpan.FromMilliseconds(20), period: TimeSpan.FromMilliseconds(20));
        await Task.Delay(150);
        int countAtDispose = grain.FireCount;
        grain.StopTimer();
        await Task.Delay(150);
        Assert.Equal(countAtDispose, grain.FireCount);
        await activation.DisposeAsync();
    }

    [Fact]
    public async Task Timer_DisposedOnGrainDeactivation()
    {
        var (grain, ctx, activation) = await SetupAsync();
        grain.StartTimer(dueTime: TimeSpan.FromMilliseconds(20), period: TimeSpan.FromMilliseconds(20));
        await Task.Delay(100);
        // Capture count AFTER deactivation so any in-flight callback that beat the dispose
        // has already settled. _disposed is set synchronously before the first await in
        // DeactivateAsync, so no further increments can happen once it returns.
        await ctx.DeactivateAsync(grain, DeactivationReason.ApplicationRequested);
        int countAfterDeactivate = grain.FireCount;
        await Task.Delay(150);
        Assert.Equal(countAfterDeactivate, grain.FireCount);
        await activation.DisposeAsync();
    }

    private sealed class TimerTestGrain : Grain
    {
        private IGrainTimer? _timer;
        public int FireCount;

        public void StartTimer(TimeSpan dueTime, TimeSpan period)
        {
            _timer = RegisterGrainTimer(
                static (state, _) =>
                {
                    Interlocked.Increment(ref state.FireCount);
                    return Task.CompletedTask;
                },
                this,
                new GrainTimerCreationOptions { DueTime = dueTime, Period = period });
        }

        public void StopTimer() => _timer?.Dispose();
    }

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
