// Copyright (c) Quark Framework. All rights reserved.

using System.Runtime.CompilerServices;

namespace Quark.Core.Streaming;

/// <summary>
/// Extension methods for reactive stream operations.
/// Provides functional operators for transforming async streams.
/// </summary>
public static class StreamOperators
{
    /// <summary>
    /// Projects each element of a stream into a new form.
    /// </summary>
    /// <typeparam name="TSource">The type of the source elements.</typeparam>
    /// <typeparam name="TResult">The type of the result elements.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A stream whose elements are the result of invoking the transform function on each element of source.</returns>
    public static async IAsyncEnumerable<TResult> Map<TSource, TResult>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource, TResult> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            yield return selector(item);
        }
    }

    /// <summary>
    /// Projects each element of a stream into a new form asynchronously.
    /// </summary>
    /// <typeparam name="TSource">The type of the source elements.</typeparam>
    /// <typeparam name="TResult">The type of the result elements.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="selector">An async transform function to apply to each element.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A stream whose elements are the result of invoking the transform function on each element of source.</returns>
    public static async IAsyncEnumerable<TResult> MapAsync<TSource, TResult>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource, Task<TResult>> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            yield return await selector(item).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Filters a stream based on a predicate.
    /// </summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A stream that contains elements from the input stream that satisfy the condition.</returns>
    public static async IAsyncEnumerable<T> Filter<T>(
        this IAsyncEnumerable<T> source,
        Func<T, bool> predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            if (predicate(item))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Filters a stream based on an async predicate.
    /// </summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="predicate">An async function to test each element for a condition.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A stream that contains elements from the input stream that satisfy the condition.</returns>
    public static async IAsyncEnumerable<T> FilterAsync<T>(
        this IAsyncEnumerable<T> source,
        Func<T, Task<bool>> predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            if (await predicate(item).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Applies an accumulator function over a stream.
    /// </summary>
    /// <typeparam name="TSource">The type of the source elements.</typeparam>
    /// <typeparam name="TAccumulate">The type of the accumulator value.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="seed">The initial accumulator value.</param>
    /// <param name="accumulator">An accumulator function to be invoked on each element.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The final accumulator value.</returns>
    public static async Task<TAccumulate> Reduce<TSource, TAccumulate>(
        this IAsyncEnumerable<TSource> source,
        TAccumulate seed,
        Func<TAccumulate, TSource, TAccumulate> accumulator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(accumulator);

        var result = seed;
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            result = accumulator(result, item);
        }

        return result;
    }

    /// <summary>
    /// Applies an async accumulator function over a stream.
    /// </summary>
    /// <typeparam name="TSource">The type of the source elements.</typeparam>
    /// <typeparam name="TAccumulate">The type of the accumulator value.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="seed">The initial accumulator value.</param>
    /// <param name="accumulator">An async accumulator function to be invoked on each element.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The final accumulator value.</returns>
    public static async Task<TAccumulate> ReduceAsync<TSource, TAccumulate>(
        this IAsyncEnumerable<TSource> source,
        TAccumulate seed,
        Func<TAccumulate, TSource, Task<TAccumulate>> accumulator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(accumulator);

        var result = seed;
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            result = await accumulator(result, item).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Groups the elements of a stream according to a specified key selector function.
    /// </summary>
    /// <typeparam name="TSource">The type of the source elements.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="keySelector">A function to extract the key for each element.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A stream of groupings where each grouping contains a key and a list of elements.</returns>
    public static async IAsyncEnumerable<System.Linq.IGrouping<TKey, TSource>> GroupByStream<TSource, TKey>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);

        var groups = new Dictionary<TKey, List<TSource>>();

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            var key = keySelector(item);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<TSource>();
                groups[key] = group;
            }
            group.Add(item);
        }

        foreach (var kvp in groups)
        {
            yield return new Grouping<TKey, TSource>(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Represents a collection of objects that have a common key.
    /// </summary>
    private sealed class Grouping<TKey, TElement> : System.Linq.IGrouping<TKey, TElement>
    {
        private readonly List<TElement> _elements;

        public TKey Key { get; }

        public Grouping(TKey key, List<TElement> elements)
        {
            Key = key;
            _elements = elements;
        }

        public IEnumerator<TElement> GetEnumerator() => _elements.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
