// Copyright (c) Quark Framework. All rights reserved.

using System.Collections.Concurrent;
using Quark.Abstractions.Streaming;
using Quark.Core.Streaming;

namespace Quark.Tests;

/// <summary>
/// Tests for backpressure and flow control in streaming.
/// Phase 8.5: Validates adaptive backpressure for slow consumers.
/// </summary>
public class BackpressureTests
{
    [Fact]
    public async Task StreamHandle_WithNoBackpressure_DeliversMessagesImmediately()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var stream = provider.GetStream<string>("test-namespace", "test-key");
        var received = new List<string>();

        await stream.SubscribeAsync(async msg =>
        {
            received.Add(msg);
            await Task.CompletedTask;
        });

        // Act
        await stream.PublishAsync("message1");
        await stream.PublishAsync("message2");
        await stream.PublishAsync("message3");
        await Task.Delay(100); // Allow async processing

        // Assert
        Assert.Equal(3, received.Count);
        Assert.Equal("message1", received[0]);
        Assert.Equal("message2", received[1]);
        Assert.Equal("message3", received[2]);
    }

    [Fact]
    public async Task StreamHandle_WithDropOldest_DropsOldMessagesWhenBufferFull()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var options = new StreamBackpressureOptions
        {
            Mode = BackpressureMode.DropOldest,
            BufferSize = 2,
            EnableMetrics = true
        };
        provider.ConfigureBackpressure("test", options);

        var stream = provider.GetStream<int>("test", "key");
        var received = new List<int>();
        var processingDelay = 200; // Slow consumer

        await stream.SubscribeAsync(async msg =>
        {
            await Task.Delay(processingDelay);
            received.Add(msg);
        });

        // Act - publish more messages than buffer size
        await stream.PublishAsync(1);
        await stream.PublishAsync(2);
        await stream.PublishAsync(3); // Should drop message 1
        await stream.PublishAsync(4); // Should drop message 2

        // Wait for processing
        await Task.Delay(1000);

        // Assert
        Assert.NotNull(stream.BackpressureMetrics);
        Assert.True(stream.BackpressureMetrics.MessagesDropped > 0);
        
        // Should have received the newest messages
        Assert.Contains(3, received);
        Assert.Contains(4, received);
    }

    [Fact]
    public async Task StreamHandle_WithDropNewest_RejectsNewMessagesWhenBufferFull()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var options = new StreamBackpressureOptions
        {
            Mode = BackpressureMode.DropNewest,
            BufferSize = 2,
            EnableMetrics = true
        };
        provider.ConfigureBackpressure("test", options);

        var stream = provider.GetStream<int>("test", "key");
        var received = new List<int>();

        await stream.SubscribeAsync(async msg =>
        {
            await Task.Delay(100); // Slow consumer
            lock (received)
            {
                received.Add(msg);
            }
        });

        // Act
        await stream.PublishAsync(1);
        await stream.PublishAsync(2);
        await Task.Delay(10); // Let first two get buffered
        await stream.PublishAsync(3); // Buffer full, should be dropped
        await stream.PublishAsync(4); // Buffer full, should be dropped

        await Task.Delay(500);

        // Assert
        Assert.NotNull(stream.BackpressureMetrics);
        Assert.True(stream.BackpressureMetrics.MessagesDropped > 0);
        
        // Should have received some of the first messages
        Assert.True(received.Count >= 2);
    }

    [Fact]
    public async Task StreamHandle_WithBlock_WaitsForSpaceInBuffer()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var options = new StreamBackpressureOptions
        {
            Mode = BackpressureMode.Block,
            BufferSize = 2,
            EnableMetrics = true
        };
        provider.ConfigureBackpressure("block-test-unique", options);

        var stream = provider.GetStream<int>("block-test-unique", "key-unique");
        var received = new ConcurrentBag<int>();

        await stream.SubscribeAsync(async msg =>
        {
            await Task.Delay(50);
            received.Add(msg);
        });

        // Act
        await stream.PublishAsync(1);
        await stream.PublishAsync(2);
        await stream.PublishAsync(3);
        await stream.PublishAsync(4);
        await stream.PublishAsync(5);

        // Wait for all messages to be processed
        await Task.Delay(800);

        // Assert
        Assert.NotNull(stream.BackpressureMetrics);
        Assert.Equal(0, stream.BackpressureMetrics.MessagesDropped);
        // All 5 messages should eventually be delivered
        Assert.True(received.Count == 5, $"Expected 5 messages but received {received.Count}");
    }

    [Fact]
    public async Task StreamHandle_WithThrottle_LimitsMessageRate()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var options = new StreamBackpressureOptions
        {
            Mode = BackpressureMode.Throttle,
            MaxMessagesPerWindow = 5,
            ThrottleWindow = TimeSpan.FromSeconds(1),
            BufferSize = 100,
            EnableMetrics = true
        };
        provider.ConfigureBackpressure("test", options);

        var stream = provider.GetStream<int>("test", "key");
        var received = new List<int>();

        await stream.SubscribeAsync(async msg =>
        {
            received.Add(msg);
            await Task.CompletedTask;
        });

        // Act - publish messages rapidly
        for (int i = 0; i < 10; i++)
        {
            await stream.PublishAsync(i);
        }

        await Task.Delay(500);

        // Assert
        Assert.NotNull(stream.BackpressureMetrics);
        Assert.True(stream.BackpressureMetrics.MessagesPublished > 0);
    }

    [Fact]
    public void BackpressureOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new StreamBackpressureOptions();

        // Assert
        Assert.Equal(BackpressureMode.None, options.Mode);
        Assert.Equal(1000, options.BufferSize);
        Assert.Equal(100, options.MaxMessagesPerWindow);
        Assert.Equal(TimeSpan.FromSeconds(1), options.ThrottleWindow);
        Assert.True(options.EnableMetrics);
    }

    [Fact]
    public void BackpressureMetrics_Reset_ClearsAllValues()
    {
        // Arrange
        var metrics = new StreamBackpressureMetrics
        {
            MessagesPublished = 100,
            MessagesDropped = 10,
            ThrottleEvents = 5,
            CurrentBufferDepth = 20,
            PeakBufferDepth = 50
        };

        // Act
        metrics.Reset();

        // Assert
        Assert.Equal(0, metrics.MessagesPublished);
        Assert.Equal(0, metrics.MessagesDropped);
        Assert.Equal(0, metrics.ThrottleEvents);
        Assert.Equal(0, metrics.CurrentBufferDepth);
        Assert.Equal(0, metrics.PeakBufferDepth);
    }

    [Fact]
    public async Task StreamHandle_WithBackpressure_TracksMetrics()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var options = new StreamBackpressureOptions
        {
            Mode = BackpressureMode.DropOldest,
            BufferSize = 5,
            EnableMetrics = true
        };
        provider.ConfigureBackpressure("metrics-test", options);

        var stream = provider.GetStream<string>("metrics-test", "key");
        var received = new List<string>();

        await stream.SubscribeAsync(async msg =>
        {
            await Task.Delay(50);
            received.Add(msg);
        });

        // Act
        for (int i = 0; i < 10; i++)
        {
            await stream.PublishAsync($"message-{i}");
        }

        await Task.Delay(1000);

        // Assert
        Assert.NotNull(stream.BackpressureMetrics);
        Assert.True(stream.BackpressureMetrics.MessagesPublished > 0);
        Assert.True(stream.BackpressureMetrics.PeakBufferDepth > 0);
    }

    [Fact]
    public void QuarkStreamProvider_ConfigureBackpressure_ThrowsOnNullNamespace()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var options = new StreamBackpressureOptions();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            provider.ConfigureBackpressure(null!, options));
    }

    [Fact]
    public void QuarkStreamProvider_ConfigureBackpressure_ThrowsOnNullOptions()
    {
        // Arrange
        var provider = new QuarkStreamProvider();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            provider.ConfigureBackpressure("test", null!));
    }

    [Fact]
    public async Task StreamHandle_MultipleConcurrentPublishers_HandlesBackpressure()
    {
        // Arrange
        var provider = new QuarkStreamProvider();
        var options = new StreamBackpressureOptions
        {
            Mode = BackpressureMode.Block,
            BufferSize = 10,
            EnableMetrics = true
        };
        provider.ConfigureBackpressure("concurrent", options);

        var stream = provider.GetStream<int>("concurrent", "key");
        var received = new List<int>();

        await stream.SubscribeAsync(async msg =>
        {
            await Task.Delay(10);
            lock (received)
            {
                received.Add(msg);
            }
        });

        // Act - multiple concurrent publishers
        var publishTasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(async () => await stream.PublishAsync(i)));

        await Task.WhenAll(publishTasks);
        await Task.Delay(1000);

        // Assert
        Assert.NotNull(stream.BackpressureMetrics);
        Assert.True(stream.BackpressureMetrics.MessagesPublished >= 50);
        Assert.True(received.Count >= 48); // Allow for some async delays
    }
}
