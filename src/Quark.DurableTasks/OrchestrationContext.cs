namespace Quark.DurableTasks;

/// <summary>
///     Context provided to orchestrations for calling activities and managing state.
/// </summary>
public sealed class OrchestrationContext
{
    private readonly IOrchestrationStateStore _stateStore;
    private readonly IActivityInvoker _activityInvoker;
    private readonly OrchestrationState _state;
    private int _replayIndex;

    /// <summary>
    ///     Gets the orchestration identifier.
    /// </summary>
    public string OrchestrationId => _state.OrchestrationId;

    /// <summary>
    ///     Gets whether the orchestration is currently in replay mode.
    /// </summary>
    public bool IsReplaying => _replayIndex < _state.History.Count;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrchestrationContext"/> class.
    /// </summary>
    public OrchestrationContext(
        OrchestrationState state,
        IOrchestrationStateStore stateStore,
        IActivityInvoker activityInvoker)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _activityInvoker = activityInvoker ?? throw new ArgumentNullException(nameof(activityInvoker));
        _replayIndex = 0;
    }

    /// <summary>
    ///     Calls an activity and returns its result.
    /// </summary>
    public async Task<byte[]> CallActivityAsync(string activityName, byte[] input)
    {
        // Check if we're replaying
        if (IsReplaying)
        {
            var replayEvent = _state.History[_replayIndex];
            _replayIndex++;

            if (replayEvent.EventType == OrchestrationEventType.ActivityCompleted &&
                replayEvent.ActivityName == activityName)
            {
                return replayEvent.Data ?? Array.Empty<byte>();
            }
            else if (replayEvent.EventType == OrchestrationEventType.ActivityFailed &&
                     replayEvent.ActivityName == activityName)
            {
                throw new InvalidOperationException($"Activity '{activityName}' failed: {replayEvent.Error}");
            }
            else
            {
                throw new InvalidOperationException($"History mismatch: expected activity event for '{activityName}'");
            }
        }

        // Not replaying - execute the activity
        try
        {
            // Add scheduled event
            var scheduledEvent = new OrchestrationEvent
            {
                EventId = _state.History.Count,
                EventType = OrchestrationEventType.ActivityScheduled,
                ActivityName = activityName,
                Data = input
            };
            _state.History.Add(scheduledEvent);
            await _stateStore.SaveStateAsync(_state);

            // Execute activity
            var result = await _activityInvoker.InvokeAsync(activityName, input);

            // Add completed event
            var completedEvent = new OrchestrationEvent
            {
                EventId = _state.History.Count,
                EventType = OrchestrationEventType.ActivityCompleted,
                ActivityName = activityName,
                Data = result
            };
            _state.History.Add(completedEvent);
            await _stateStore.SaveStateAsync(_state);

            _replayIndex++;
            return result;
        }
        catch (Exception ex)
        {
            // Add failed event
            var failedEvent = new OrchestrationEvent
            {
                EventId = _state.History.Count,
                EventType = OrchestrationEventType.ActivityFailed,
                ActivityName = activityName,
                Error = ex.Message
            };
            _state.History.Add(failedEvent);
            await _stateStore.SaveStateAsync(_state);

            _replayIndex++;
            throw;
        }
    }

    /// <summary>
    ///     Calls a sub-orchestration and returns its result.
    /// </summary>
    public async Task<byte[]> CallSubOrchestrationAsync(string orchestrationName, byte[] input)
    {
        // Similar to CallActivityAsync but for sub-orchestrations
        // Implementation would delegate to an orchestration manager
        throw new NotImplementedException("Sub-orchestrations not yet implemented");
    }

    /// <summary>
    ///     Creates a durable timer.
    /// </summary>
    public async Task CreateTimerAsync(TimeSpan delay)
    {
        if (IsReplaying)
        {
            var replayEvent = _state.History[_replayIndex];
            _replayIndex++;

            if (replayEvent.EventType == OrchestrationEventType.TimerFired)
            {
                return;
            }
            else
            {
                throw new InvalidOperationException("History mismatch: expected timer fired event");
            }
        }

        // Create timer
        var timerCreatedEvent = new OrchestrationEvent
        {
            EventId = _state.History.Count,
            EventType = OrchestrationEventType.TimerCreated
        };
        _state.History.Add(timerCreatedEvent);
        await _stateStore.SaveStateAsync(_state);

        // Wait for the delay
        await Task.Delay(delay);

        // Add timer fired event
        var timerFiredEvent = new OrchestrationEvent
        {
            EventId = _state.History.Count,
            EventType = OrchestrationEventType.TimerFired
        };
        _state.History.Add(timerFiredEvent);
        await _stateStore.SaveStateAsync(_state);

        _replayIndex++;
    }

    /// <summary>
    ///     Waits for an external event.
    /// </summary>
    public async Task<byte[]> WaitForExternalEventAsync(string eventName)
    {
        if (IsReplaying)
        {
            var replayEvent = _state.History[_replayIndex];
            _replayIndex++;

            if (replayEvent.EventType == OrchestrationEventType.ExternalEventReceived)
            {
                return replayEvent.Data ?? Array.Empty<byte>();
            }
            else
            {
                throw new InvalidOperationException("History mismatch: expected external event");
            }
        }

        // Check if event has already been raised
        if (_state.PendingExternalEvents.TryGetValue(eventName, out var eventData))
        {
            _state.PendingExternalEvents.Remove(eventName);

            var receivedEvent = new OrchestrationEvent
            {
                EventId = _state.History.Count,
                EventType = OrchestrationEventType.ExternalEventReceived,
                Data = eventData
            };
            _state.History.Add(receivedEvent);
            await _stateStore.SaveStateAsync(_state);

            _replayIndex++;
            return eventData;
        }

        // Mark as suspended waiting for event
        _state.Status = OrchestrationStatus.Suspended;
        await _stateStore.SaveStateAsync(_state);

        // In a real implementation, this would suspend the orchestration
        // and resume when the event is raised
        throw new NotImplementedException("External event waiting not yet implemented");
    }
}
