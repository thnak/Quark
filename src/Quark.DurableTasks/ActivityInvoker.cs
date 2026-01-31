using System.Collections.Concurrent;
using System.Text.Json;

namespace Quark.DurableTasks;

/// <summary>
///     Default activity invoker that executes registered activities.
/// </summary>
public sealed class ActivityInvoker : IActivityInvoker
{
    private readonly ConcurrentDictionary<string, Func<byte[], CancellationToken, Task<byte[]>>> _activities = new();
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ActivityInvoker"/> class.
    /// </summary>
    public ActivityInvoker(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    /// <summary>
    ///     Registers an activity for execution.
    /// </summary>
    public void RegisterActivity<TInput, TOutput>(IActivity<TInput, TOutput> activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        _activities[activity.Name] = async (inputBytes, ct) =>
        {
            var input = JsonSerializer.Deserialize<TInput>(inputBytes, _jsonOptions);
            if (input == null)
            {
                throw new InvalidOperationException($"Failed to deserialize input for activity '{activity.Name}'");
            }

            var output = await activity.ExecuteAsync(input, ct);
            return JsonSerializer.SerializeToUtf8Bytes(output, _jsonOptions);
        };
    }

    /// <inheritdoc />
    public async Task<byte[]> InvokeAsync(string activityName, byte[] input, CancellationToken cancellationToken = default)
    {
        if (!_activities.TryGetValue(activityName, out var activityFunc))
        {
            throw new InvalidOperationException($"Activity '{activityName}' is not registered");
        }

        return await activityFunc(input, cancellationToken);
    }

    /// <summary>
    ///     Gets the number of registered activities.
    /// </summary>
    public int ActivityCount => _activities.Count;
}
