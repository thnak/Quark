using System.Text.Json;

namespace Quark.DurableTasks;

/// <summary>
///     Base class for durable task orchestrations.
///     Orchestrations coordinate activities and maintain state across restarts.
/// </summary>
public abstract class OrchestrationBase
{
    private readonly OrchestrationContext _context;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrchestrationBase"/> class.
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    protected OrchestrationBase(OrchestrationContext context, JsonSerializerOptions? jsonOptions = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    /// <summary>
    ///     Gets the orchestration identifier.
    /// </summary>
    protected string OrchestrationId => _context.OrchestrationId;

    /// <summary>
    ///     Calls an activity function and waits for its result.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="activityName">The name of the activity to call.</param>
    /// <param name="input">The activity input.</param>
    /// <returns>The activity output.</returns>
    protected async Task<TOutput> CallActivityAsync<TInput, TOutput>(string activityName, TInput input)
    {
        ArgumentException.ThrowIfNullOrEmpty(activityName);

        var inputBytes = JsonSerializer.SerializeToUtf8Bytes(input, _jsonOptions);
        var outputBytes = await _context.CallActivityAsync(activityName, inputBytes);

        var output = JsonSerializer.Deserialize<TOutput>(outputBytes, _jsonOptions);
        return output ?? throw new InvalidOperationException($"Activity '{activityName}' returned null");
    }

    /// <summary>
    ///     Calls a sub-orchestration and waits for its result.
    /// </summary>
    /// <typeparam name="TInput">The sub-orchestration input type.</typeparam>
    /// <typeparam name="TOutput">The sub-orchestration output type.</typeparam>
    /// <param name="orchestrationName">The name of the orchestration to call.</param>
    /// <param name="input">The orchestration input.</param>
    /// <returns>The orchestration output.</returns>
    protected async Task<TOutput> CallSubOrchestrationAsync<TInput, TOutput>(string orchestrationName, TInput input)
    {
        ArgumentException.ThrowIfNullOrEmpty(orchestrationName);

        var inputBytes = JsonSerializer.SerializeToUtf8Bytes(input, _jsonOptions);
        var outputBytes = await _context.CallSubOrchestrationAsync(orchestrationName, inputBytes);

        var output = JsonSerializer.Deserialize<TOutput>(outputBytes, _jsonOptions);
        return output ?? throw new InvalidOperationException($"Sub-orchestration '{orchestrationName}' returned null");
    }

    /// <summary>
    ///     Creates a durable timer that survives orchestration restarts.
    /// </summary>
    /// <param name="delay">The delay before the timer fires.</param>
    protected Task CreateTimerAsync(TimeSpan delay)
    {
        return _context.CreateTimerAsync(delay);
    }

    /// <summary>
    ///     Waits for an external event to be raised.
    /// </summary>
    /// <typeparam name="TEvent">The event data type.</typeparam>
    /// <param name="eventName">The name of the event to wait for.</param>
    /// <returns>The event data when received.</returns>
    protected async Task<TEvent> WaitForExternalEventAsync<TEvent>(string eventName)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventName);

        var eventBytes = await _context.WaitForExternalEventAsync(eventName);
        var eventData = JsonSerializer.Deserialize<TEvent>(eventBytes, _jsonOptions);
        return eventData ?? throw new InvalidOperationException($"External event '{eventName}' data was null");
    }

    /// <summary>
    ///     When overridden in a derived class, implements the orchestration logic.
    /// </summary>
    /// <param name="input">The serialized orchestration input.</param>
    /// <returns>The serialized orchestration output.</returns>
    public abstract Task<byte[]> RunAsync(byte[] input);
}
