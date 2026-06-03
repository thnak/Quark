using System.Diagnostics;
using Quark.Tests.Unit.Integration;
using Xunit;

namespace Quark.Tests.Unit.Diagnostics;

public sealed class ActivityPropagationTests : IAsyncDisposable
{
    private readonly GrainCallFixture _fixture = new();

    [Fact]
    public async Task GrainCall_CreatesActivity_WithExpectedTags()
    {
        var capturedActivities = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Quark.Runtime",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => capturedActivities.Add(a)
        };
        ActivitySource.AddActivityListener(listener);

        ICounterGrain grain = _fixture.Client.GetGrain<ICounterGrain>("propagation-test");
        await grain.IncrementAsync();

        // Find the activity for our specific grain call
        Activity? captured = capturedActivities.FirstOrDefault(a =>
            a.Tags.Any(t => t.Key == "grain.key" && t.Value?.ToString() == "propagation-test"));

        Assert.NotNull(captured);
        Assert.Equal("grain.invoke", captured!.OperationName);
        Assert.Contains(captured.Tags, t => t.Key == "grain.type");
        Assert.Contains(captured.Tags, t => t.Key == "grain.key" && t.Value?.ToString() == "propagation-test");
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
