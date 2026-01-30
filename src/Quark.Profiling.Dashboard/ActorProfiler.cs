using System.Collections.Concurrent;
using Quark.Profiling.Abstractions;

namespace Quark.Profiling.Dashboard;

/// <summary>
/// Default implementation of actor profiler.
/// Thread-safe, lock-free implementation using concurrent collections.
/// </summary>
public sealed class ActorProfiler : IActorProfiler
{
    private readonly ConcurrentDictionary<string, ActorProfilingData> _profilingData = new();

    /// <inheritdoc/>
    public void StartProfiling(string actorType, string actorId)
    {
        var key = GetKey(actorType, actorId);
        _profilingData.TryAdd(key, new ActorProfilingData
        {
            ActorType = actorType,
            ActorId = actorId,
            StartTime = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc/>
    public void StopProfiling(string actorType, string actorId)
    {
        var key = GetKey(actorType, actorId);
        _profilingData.TryRemove(key, out _);
    }

    /// <inheritdoc/>
    public void RecordMethodInvocation(string actorType, string actorId, string methodName, double durationMs)
    {
        var key = GetKey(actorType, actorId);
        
        var data = _profilingData.GetOrAdd(key, _ => new ActorProfilingData
        {
            ActorType = actorType,
            ActorId = actorId,
            StartTime = DateTimeOffset.UtcNow
        });

        lock (data)
        {
            data.TotalInvocations++;
            data.TotalDurationMs += durationMs;
            data.MinDurationMs = Math.Min(data.MinDurationMs, durationMs);
            data.MaxDurationMs = Math.Max(data.MaxDurationMs, durationMs);

            var methodData = data.Methods.GetOrAdd(methodName, _ => new MethodProfilingData
            {
                MethodName = methodName
            });

            methodData.InvocationCount++;
            methodData.TotalDurationMs += durationMs;
            methodData.MinDurationMs = Math.Min(methodData.MinDurationMs, durationMs);
            methodData.MaxDurationMs = Math.Max(methodData.MaxDurationMs, durationMs);
        }
    }

    /// <inheritdoc/>
    public void RecordAllocation(string actorType, string actorId, long bytes)
    {
        var key = GetKey(actorType, actorId);
        
        var data = _profilingData.GetOrAdd(key, _ => new ActorProfilingData
        {
            ActorType = actorType,
            ActorId = actorId,
            StartTime = DateTimeOffset.UtcNow
        });

        lock (data)
        {
            data.TotalAllocations += bytes;
        }
    }

    /// <inheritdoc/>
    public ActorProfilingData? GetProfilingData(string actorType, string actorId)
    {
        var key = GetKey(actorType, actorId);
        _profilingData.TryGetValue(key, out var data);
        return data;
    }

    /// <inheritdoc/>
    public IEnumerable<ActorProfilingData> GetProfilingDataByType(string actorType)
    {
        return _profilingData.Values.Where(d => d.ActorType == actorType);
    }

    /// <inheritdoc/>
    public IEnumerable<ActorProfilingData> GetAllProfilingData()
    {
        return _profilingData.Values;
    }

    /// <inheritdoc/>
    public void ClearProfilingData(string actorType, string actorId)
    {
        var key = GetKey(actorType, actorId);
        _profilingData.TryRemove(key, out _);
    }

    /// <inheritdoc/>
    public void ClearAllProfilingData()
    {
        _profilingData.Clear();
    }

    private static string GetKey(string actorType, string actorId) => $"{actorType}:{actorId}";
}

/// <summary>
/// Extension methods for Dictionary to add GetOrAdd functionality.
/// </summary>
internal static class DictionaryExtensions
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> factory)
        where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var value))
        {
            value = factory(key);
            dict[key] = value;
        }
        return value;
    }
}
