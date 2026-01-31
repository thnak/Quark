// Copyright (c) Quark Framework. All rights reserved.

using Quark.Core.Streaming;

namespace Quark.Tests;

/// <summary>
/// Tests for stream operator extensions (Map, Filter, Reduce, GroupBy).
/// </summary>
public class StreamOperatorsTests
{
    [Fact]
    public async Task Map_TransformsElements()
    {
        // Arrange
        var source = GenerateSequence(1, 5);

        // Act
        var results = new List<int>();
        await foreach (var item in source.Map(x => x * 2))
        {
            results.Add(item);
        }

        // Assert
        Assert.Equal(new[] { 2, 4, 6, 8, 10 }, results);
    }

    [Fact]
    public async Task MapAsync_TransformsElementsAsynchronously()
    {
        // Arrange
        var source = GenerateSequence(1, 3);

        // Act
        var results = new List<int>();
        await foreach (var item in source.MapAsync(async x =>
        {
            await Task.Delay(10);
            return x * 3;
        }))
        {
            results.Add(item);
        }

        // Assert
        Assert.Equal(new[] { 3, 6, 9 }, results);
    }

    [Fact]
    public async Task Filter_FiltersElements()
    {
        // Arrange
        var source = GenerateSequence(1, 10);

        // Act
        var results = new List<int>();
        await foreach (var item in source.Filter(x => x % 2 == 0))
        {
            results.Add(item);
        }

        // Assert
        Assert.Equal(new[] { 2, 4, 6, 8, 10 }, results);
    }

    [Fact]
    public async Task FilterAsync_FiltersElementsAsynchronously()
    {
        // Arrange
        var source = GenerateSequence(1, 5);

        // Act
        var results = new List<int>();
        await foreach (var item in source.FilterAsync(async x =>
        {
            await Task.Delay(10);
            return x > 2;
        }))
        {
            results.Add(item);
        }

        // Assert
        Assert.Equal(new[] { 3, 4, 5 }, results);
    }

    [Fact]
    public async Task Reduce_AggregatesElements()
    {
        // Arrange
        var source = GenerateSequence(1, 5);

        // Act
        var sum = await source.Reduce(0, (acc, x) => acc + x);

        // Assert
        Assert.Equal(15, sum); // 1+2+3+4+5
    }

    [Fact]
    public async Task ReduceAsync_AggregatesElementsAsynchronously()
    {
        // Arrange
        var source = GenerateSequence(1, 4);

        // Act
        var product = await source.ReduceAsync(1, async (acc, x) =>
        {
            await Task.Delay(10);
            return acc * x;
        });

        // Assert
        Assert.Equal(24, product); // 1*2*3*4
    }

    [Fact]
    public async Task GroupBy_GroupsElementsByKey()
    {
        // Arrange
        var source = GenerateSequence(1, 10);

        // Act
        var groups = new List<System.Linq.IGrouping<bool, int>>();
        await foreach (var group in source.GroupByStream(x => x % 2 == 0))
        {
            groups.Add(group);
        }

        // Assert
        Assert.Equal(2, groups.Count);
        
        var evenGroup = groups.FirstOrDefault(g => g.Key == true);
        var oddGroup = groups.FirstOrDefault(g => g.Key == false);
        
        Assert.NotNull(evenGroup);
        Assert.NotNull(oddGroup);
        Assert.Equal(5, evenGroup.Count());
        Assert.Equal(5, oddGroup.Count());
    }

    [Fact]
    public async Task Map_WithNullSource_ThrowsArgumentNullException()
    {
        // Arrange
        IAsyncEnumerable<int>? source = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in source!.Map(x => x * 2))
            {
            }
        });
    }

    [Fact]
    public async Task Filter_WithNullPredicate_ThrowsArgumentNullException()
    {
        // Arrange
        var source = GenerateSequence(1, 5);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in source.Filter(null!))
            {
            }
        });
    }

    [Fact]
    public async Task Reduce_WithNullAccumulator_ThrowsArgumentNullException()
    {
        // Arrange
        var source = GenerateSequence(1, 5);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await source.Reduce(0, null!);
        });
    }

    [Fact]
    public async Task CombinedOperators_MapAndFilter_WorksTogether()
    {
        // Arrange
        var source = GenerateSequence(1, 10);

        // Act
        var results = new List<int>();
        await foreach (var item in source
            .Map(x => x * 2)      // Double each
            .Filter(x => x > 10))  // Keep only > 10
        {
            results.Add(item);
        }

        // Assert
        Assert.Equal(new[] { 12, 14, 16, 18, 20 }, results);
    }

    // Helper method
    private static async IAsyncEnumerable<int> GenerateSequence(int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.CompletedTask;
            yield return start + i;
        }
    }
}
