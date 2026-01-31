// Copyright (c) Quark Framework. All rights reserved.

using Quark.Abstractions.Streaming;
using Quark.Core.Streaming;

namespace Quark.Tests;

/// <summary>
/// Tests for windowing extensions on async streams.
/// </summary>
public class WindowingExtensionsTests
{
    [Fact]
    public async Task Window_TimeBasedWindowing_GroupsMessagesByTime()
    {
        // Arrange
        var source = GenerateMessages(5, delayMs: 50);

        // Act
        var windows = new List<Window<int>>();
        await foreach (var window in source.Window(TimeSpan.FromMilliseconds(200)))
        {
            windows.Add(window);
        }

        // Assert
        Assert.NotEmpty(windows);
        Assert.True(windows.All(w => w.Type == WindowType.Time));
    }

    [Fact]
    public async Task Window_CountBasedWindowing_GroupsMessagesByCount()
    {
        // Arrange
        var source = GenerateMessages(10, delayMs: 10);

        // Act
        var windows = new List<Window<int>>();
        await foreach (var window in source.Window(3))
        {
            windows.Add(window);
        }

        // Assert
        Assert.Equal(4, windows.Count); // 10 messages: 3+3+3+1
        Assert.Equal(3, windows[0].Messages.Count);
        Assert.Equal(3, windows[1].Messages.Count);
        Assert.Equal(3, windows[2].Messages.Count);
        Assert.Equal(1, windows[3].Messages.Count);
        Assert.True(windows.All(w => w.Type == WindowType.Count));
    }

    [Fact]
    public async Task SlidingWindow_CreatesOverlappingWindows()
    {
        // Arrange
        var source = GenerateMessages(5, delayMs: 10);

        // Act
        var windows = new List<Window<int>>();
        await foreach (var window in source.SlidingWindow(windowSize: 3, slide: 1))
        {
            windows.Add(window);
        }

        // Assert
        Assert.True(windows.Count >= 3); // At least 3 sliding windows
        Assert.True(windows.All(w => w.Type == WindowType.Sliding));
    }

    [Fact]
    public async Task SessionWindow_GroupsMessagesByInactivity()
    {
        // Arrange
        var source = GenerateMessagesWithGap();

        // Act
        var windows = new List<Window<int>>();
        await foreach (var window in source.SessionWindow(TimeSpan.FromMilliseconds(100)))
        {
            windows.Add(window);
        }

        // Assert
        Assert.True(windows.Count >= 2); // At least 2 sessions due to gap
        Assert.True(windows.All(w => w.Type == WindowType.Session));
    }

    [Fact]
    public async Task Window_WithZeroDuration_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var source = GenerateMessages(5, delayMs: 10);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in source.Window(TimeSpan.Zero))
            {
            }
        });
    }

    [Fact]
    public async Task Window_WithZeroCount_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var source = GenerateMessages(5, delayMs: 10);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in source.Window(0))
            {
            }
        });
    }

    [Fact]
    public async Task Window_WithNullSource_ThrowsArgumentNullException()
    {
        // Arrange
        IAsyncEnumerable<int>? source = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in source!.Window(TimeSpan.FromSeconds(1)))
            {
            }
        });
    }

    // Helper methods
    private static async IAsyncEnumerable<int> GenerateMessages(int count, int delayMs)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.Delay(delayMs);
            yield return i;
        }
    }

    private static async IAsyncEnumerable<int> GenerateMessagesWithGap()
    {
        // First group
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(20);
            yield return i;
        }

        // Gap
        await Task.Delay(150);

        // Second group
        for (int i = 3; i < 6; i++)
        {
            await Task.Delay(20);
            yield return i;
        }
    }
}
