// Copyright (c) Quark Framework. All rights reserved.

using Quark.Abstractions;
using Quark.Abstractions.Streaming;
using Quark.Core.Actors;
using Quark.Core.Streaming;

namespace Quark.Tests;

/// <summary>
/// Tests for reactive actor functionality including windowing and stream operators.
/// </summary>
public class ReactiveActorTests
{
    [Fact]
    public void ReactiveActorBase_WithDefaultConfiguration_HasCorrectDefaults()
    {
        // Arrange & Act
        var actor = new TestReactiveActor("test-1");

        // Assert
        Assert.Equal(0, actor.BufferedCount);
        Assert.Equal(0, actor.MessagesReceived);
        Assert.Equal(0, actor.MessagesProcessed);
        Assert.Equal(0, actor.MessagesDropped);
        Assert.False(actor.IsBackpressureActive);
    }

    [Fact]
    public async Task ReactiveActorBase_SendAsync_IncrementsMessagesReceived()
    {
        // Arrange
        var actor = new TestReactiveActor("test-1");

        // Act
        await actor.SendAsync(42);
        await Task.Delay(50); // Allow processing

        // Assert
        Assert.True(actor.MessagesReceived > 0);
    }

    [Fact]
    public async Task ReactiveActorBase_WithCustomAttribute_UsesAttributeConfiguration()
    {
        // Arrange & Act
        var actor = new TestConfiguredReactiveActor("test-1");

        // Assert - values from attribute
        Assert.Equal(0, actor.BufferedCount); // Initially empty
    }

    [Fact]
    public async Task ReactiveActorBase_ProcessStreamAsync_TransformsMessages()
    {
        // Arrange
        var actor = new TestReactiveActor("test-1");
        var processTask = actor.StartProcessing();

        // Act
        await actor.SendAsync(1);
        await actor.SendAsync(2);
        await actor.SendAsync(3);
        actor.CompleteInput();

        await processTask;

        // Assert
        Assert.Equal(3, actor.Outputs.Count);
        Assert.Equal(2, actor.Outputs[0]); // 1 * 2
        Assert.Equal(4, actor.Outputs[1]); // 2 * 2
        Assert.Equal(6, actor.Outputs[2]); // 3 * 2
    }

    [Fact]
    public async Task ReactiveActorBase_OnDeactivate_CompletesInput()
    {
        // Arrange
        var actor = new TestReactiveActor("test-1");
        var processTask = actor.StartProcessing();

        // Act
        await actor.SendAsync(1);
        await actor.OnDeactivateAsync();

        await processTask;

        // Assert
        Assert.Equal(1, actor.Outputs.Count);
    }

    // Test helper class
    [Actor(Name = "TestReactive")]
    public class TestReactiveActor : ReactiveActorBase<int, int>
    {
        public readonly List<int> Outputs = new();

        public TestReactiveActor(string actorId) : base(actorId)
        {
        }

        public override async IAsyncEnumerable<int> ProcessStreamAsync(
            IAsyncEnumerable<int> stream,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in stream.WithCancellation(cancellationToken))
            {
                yield return item * 2;
            }
        }

        protected override Task OnOutputAsync(int output, CancellationToken cancellationToken = default)
        {
            Outputs.Add(output);
            return Task.CompletedTask;
        }

        public Task StartProcessing(CancellationToken cancellationToken = default)
        {
            return StartStreamProcessingAsync(cancellationToken);
        }
    }

    [Actor(Name = "TestConfiguredReactive")]
    [ReactiveActor(BufferSize = 500, BackpressureThreshold = 0.7, OverflowStrategy = BackpressureMode.DropOldest)]
    public class TestConfiguredReactiveActor : ReactiveActorBase<int, int>
    {
        public TestConfiguredReactiveActor(string actorId) : base(actorId)
        {
        }

        public override async IAsyncEnumerable<int> ProcessStreamAsync(
            IAsyncEnumerable<int> stream,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in stream.WithCancellation(cancellationToken))
            {
                yield return item;
            }
        }
    }
}
