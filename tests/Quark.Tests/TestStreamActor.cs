using Quark.Abstractions;
using Quark.Abstractions.Streaming;
using Quark.Core.Actors;

namespace Quark.Tests;

/// <summary>
/// Test actor for stream testing.
/// </summary>
[Actor(Name = "TestStream")]
[QuarkStream("orders/processed")]
public class TestStreamActor : ActorBase, IStreamConsumer<string>
{
    public List<string> ReceivedMessages { get; } = new();

    public TestStreamActor(string actorId) : base(actorId)
    {
    }

    public Task OnStreamMessageAsync(string message, StreamId streamId, CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add(message);
        return Task.CompletedTask;
    }
}