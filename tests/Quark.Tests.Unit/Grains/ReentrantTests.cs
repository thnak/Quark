using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class ReentrantTests
{
    private static GrainActivation MakeActivation(bool isReentrant)
    {
        var grainId = new GrainId(new GrainType("G"), "1");
        return new GrainActivation(grainId, grainId.Type, isReentrant,
            new NullServiceProvider(), NullLogger<GrainActivation>.Instance);
    }

    [Fact]
    public async Task NonReentrantGrain_SerializesDispatch()
    {
        await using var activation = MakeActivation(isReentrant: false);

        var completionSource1 = new TaskCompletionSource();
        var completionSource2 = new TaskCompletionSource();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        _ = activation.PostAsync(async () =>
        {
            await Task.Delay(50);
            completionSource1.SetResult();
        });

        _ = activation.PostAsync(async () =>
        {
            await Task.Delay(50);
            completionSource2.SetResult();
        });

        await Task.WhenAll(completionSource1.Task, completionSource2.Task);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 80,
            $"Expected ~100ms serial, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ReentrantGrain_AllowsConcurrentDispatch()
    {
        await using var activation = MakeActivation(isReentrant: true);

        using var gate = new SemaphoreSlim(0, 1);
        var task2Reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = activation.PostAsync(async () =>
        {
            await gate.WaitAsync(TimeSpan.FromSeconds(5));
        });

        var task2 = activation.PostAsync(() =>
        {
            task2Reached.SetResult();
            return Task.CompletedTask;
        });

        await task2Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.Release();
        await Task.WhenAll(task1.AsTask(), task2.AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
