# Timers and Reminders

Quark provides two mechanisms for scheduling temporal work in actors: **Timers** (in-memory, volatile) and **Reminders** (persistent, durable). Both enable actors to execute recurring or delayed operations.

## Overview

| Feature | Timers | Reminders |
|---------|--------|-----------|
| **Persistence** | ❌ In-memory only | ✅ Stored in persistent storage |
| **Survival** | Lost on restart | ✅ Survive actor/silo restarts |
| **Performance** | ⚡ Faster (no I/O) | 🐢 Slower (persistence overhead) |
| **Use Case** | Transient, frequent tasks | Important, infrequent tasks |
| **Activation** | Requires active actor | ✅ Activates deactivated actors |
| **Implementation** | `System.Threading.Timer` | Database/Redis |

## Timers (Volatile)

Timers are lightweight, in-memory scheduling mechanisms. They're perfect for frequent callbacks that don't need to survive restarts.

### IActorTimer Interface

```csharp
namespace Quark.Abstractions.Timers;

public interface IActorTimer : IDisposable
{
    /// <summary>
    /// Gets the timer name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the timer is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts or restarts the timer.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the timer.
    /// </summary>
    void Stop();
}
```

### IActorTimerManager Interface

```csharp
public interface IActorTimerManager
{
    /// <summary>
    /// Registers a new timer for the actor.
    /// </summary>
    IActorTimer RegisterTimer(
        string name,
        Func<CancellationToken, Task> callback,
        TimeSpan dueTime,
        TimeSpan period);

    /// <summary>
    /// Unregisters a timer by name.
    /// </summary>
    void UnregisterTimer(string name);

    /// <summary>
    /// Gets all registered timers.
    /// </summary>
    IReadOnlyCollection<IActorTimer> GetTimers();
}
```

### Using Timers

#### Basic Timer Example

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

[Actor(Name = "HeartbeatMonitor")]
public class HeartbeatActor : ActorBase
{
    private IActorTimer? _heartbeatTimer;

    public HeartbeatActor(string actorId, IActorTimerManager? timerManager = null) 
        : base(actorId)
    {
        TimerManager = timerManager;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{ActorId}: Starting heartbeat");

        // Register a timer that fires every 5 seconds
        _heartbeatTimer = TimerManager?.RegisterTimer(
            name: "heartbeat",
            callback: OnHeartbeatAsync,
            dueTime: TimeSpan.FromSeconds(5),
            period: TimeSpan.FromSeconds(5)
        );

        _heartbeatTimer?.Start();
        return Task.CompletedTask;
    }

    private Task OnHeartbeatAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"{ActorId}: Heartbeat at {DateTime.UtcNow:HH:mm:ss}");
        // Perform health check, send metrics, etc.
        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{ActorId}: Stopping heartbeat");
        _heartbeatTimer?.Stop();
        _heartbeatTimer?.Dispose();
        return Task.CompletedTask;
    }
}
```

#### One-Shot Timer (Delayed Execution)

```csharp
[Actor]
public class SessionActor : ActorBase
{
    private IActorTimer? _timeoutTimer;

    public void StartSession(TimeSpan timeout)
    {
        Console.WriteLine($"Session started with {timeout.TotalMinutes} minute timeout");

        // One-shot timer: fires once after the timeout period
        _timeoutTimer = TimerManager?.RegisterTimer(
            name: "session-timeout",
            callback: OnSessionTimeoutAsync,
            dueTime: timeout,
            period: Timeout.InfiniteTimeSpan // Don't repeat
        );

        _timeoutTimer?.Start();
    }

    private Task OnSessionTimeoutAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Session expired - cleaning up");
        // Clean up session data, notify user, etc.
        return OnDeactivateAsync(cancellationToken);
    }

    public void ExtendSession(TimeSpan additionalTime)
    {
        // Restart timer with new duration
        _timeoutTimer?.Stop();
        _timeoutTimer = TimerManager?.RegisterTimer(
            name: "session-timeout",
            callback: OnSessionTimeoutAsync,
            dueTime: additionalTime,
            period: Timeout.InfiniteTimeSpan
        );
        _timeoutTimer?.Start();
    }
}
```

#### Multiple Timers

```csharp
[Actor]
public class GameActor : ActorBase
{
    private IActorTimer? _tickTimer;
    private IActorTimer? _saveTimer;
    private IActorTimer? _cleanupTimer;

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Game tick: 60 times per second
        _tickTimer = TimerManager?.RegisterTimer(
            "game-tick",
            OnGameTickAsync,
            TimeSpan.FromMilliseconds(16.67), // ~60 FPS
            TimeSpan.FromMilliseconds(16.67)
        );

        // Auto-save: every 5 minutes
        _saveTimer = TimerManager?.RegisterTimer(
            "auto-save",
            OnAutoSaveAsync,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5)
        );

        // Cleanup: every hour
        _cleanupTimer = TimerManager?.RegisterTimer(
            "cleanup",
            OnCleanupAsync,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1)
        );

        _tickTimer?.Start();
        _saveTimer?.Start();
        _cleanupTimer?.Start();

        return Task.CompletedTask;
    }

    private Task OnGameTickAsync(CancellationToken ct)
    {
        // Update game state
        return Task.CompletedTask;
    }

    private Task OnAutoSaveAsync(CancellationToken ct)
    {
        // Save game state
        return Task.CompletedTask;
    }

    private Task OnCleanupAsync(CancellationToken ct)
    {
        // Clean up old data
        return Task.CompletedTask;
    }
}
```

## Reminders (Persistent)

Reminders are persistent scheduling mechanisms that survive actor and silo restarts. They're ideal for important, infrequent tasks.

### IRemindable Interface

```csharp
namespace Quark.Abstractions.Reminders;

public interface IRemindable
{
    /// <summary>
    /// Called when a reminder fires.
    /// </summary>
    /// <param name="reminderName">The name of the reminder that fired.</param>
    /// <param name="data">Optional data associated with the reminder.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ReceiveReminderAsync(
        string reminderName, 
        byte[]? data, 
        CancellationToken cancellationToken = default);
}
```

### IReminderTable Interface

```csharp
public interface IReminderTable
{
    /// <summary>
    /// Registers a reminder.
    /// </summary>
    Task RegisterReminderAsync(Reminder reminder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a reminder.
    /// </summary>
    Task UnregisterReminderAsync(
        string actorType, 
        string actorId, 
        string reminderName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all reminders for an actor.
    /// </summary>
    Task<IReadOnlyCollection<Reminder>> GetRemindersAsync(
        string actorType, 
        string actorId,
        CancellationToken cancellationToken = default);
}
```

### Reminder Structure

```csharp
public class Reminder
{
    public string ActorType { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string ReminderName { get; set; } = "";
    public TimeSpan DueTime { get; set; }
    public TimeSpan Period { get; set; }
    public byte[]? Data { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? NextFireTime { get; set; }
}
```

### Using Reminders

#### Basic Reminder Example

```csharp
using Quark.Abstractions;
using Quark.Abstractions.Reminders;
using Quark.Core.Actors;

[Actor(Name = "SubscriptionActor")]
public class SubscriptionActor : ActorBase, IRemindable
{
    private readonly IReminderTable _reminderTable;

    public SubscriptionActor(
        string actorId, 
        IReminderTable reminderTable) 
        : base(actorId)
    {
        _reminderTable = reminderTable;
    }

    public async Task SubscribeAsync(string plan, DateTime renewalDate)
    {
        Console.WriteLine($"User {ActorId} subscribed to {plan}");

        // Calculate when the subscription should renew
        var dueTime = renewalDate - DateTime.UtcNow;

        // Register a reminder for renewal
        var reminder = new Reminder
        {
            ActorType = "SubscriptionActor",
            ActorId = ActorId,
            ReminderName = "subscription-renewal",
            DueTime = dueTime,
            Period = TimeSpan.FromDays(30), // Renew monthly
            Data = Encoding.UTF8.GetBytes(plan),
            RegisteredAt = DateTimeOffset.UtcNow
        };

        await _reminderTable.RegisterReminderAsync(reminder);
        Console.WriteLine($"Renewal reminder set for {renewalDate}");
    }

    public async Task ReceiveReminderAsync(
        string reminderName, 
        byte[]? data, 
        CancellationToken cancellationToken = default)
    {
        if (reminderName == "subscription-renewal")
        {
            var plan = data != null ? Encoding.UTF8.GetString(data) : "unknown";
            Console.WriteLine($"Processing renewal for {ActorId}, plan: {plan}");

            // Process payment, extend subscription, etc.
            await ProcessRenewalAsync(plan, cancellationToken);

            Console.WriteLine($"Renewal complete. Next renewal in 30 days.");
        }
    }

    private Task ProcessRenewalAsync(string plan, CancellationToken ct)
    {
        // Charge payment method, update subscription status, etc.
        return Task.CompletedTask;
    }

    public async Task CancelSubscriptionAsync()
    {
        // Remove the renewal reminder
        await _reminderTable.UnregisterReminderAsync(
            actorType: "SubscriptionActor",
            actorId: ActorId,
            reminderName: "subscription-renewal"
        );

        Console.WriteLine($"Subscription cancelled for {ActorId}");
    }
}
```

#### One-Time Reminder (Delayed Task)

```csharp
[Actor]
public class OrderActor : ActorBase, IRemindable
{
    private readonly IReminderTable _reminderTable;

    public OrderActor(string actorId, IReminderTable reminderTable) 
        : base(actorId)
    {
        _reminderTable = reminderTable;
    }

    public async Task PlaceOrderAsync(Order order)
    {
        Console.WriteLine($"Order {ActorId} placed. Auto-cancel in 30 minutes if not confirmed.");

        // Set a one-time reminder
        var reminder = new Reminder
        {
            ActorType = "OrderActor",
            ActorId = ActorId,
            ReminderName = "auto-cancel",
            DueTime = TimeSpan.FromMinutes(30),
            Period = Timeout.InfiniteTimeSpan, // One-time only
            Data = SerializeOrder(order)
        };

        await _reminderTable.RegisterReminderAsync(reminder);
    }

    public async Task ConfirmOrderAsync()
    {
        Console.WriteLine($"Order {ActorId} confirmed");

        // Cancel the auto-cancel reminder
        await _reminderTable.UnregisterReminderAsync(
            "OrderActor", 
            ActorId, 
            "auto-cancel"
        );
    }

    public async Task ReceiveReminderAsync(
        string reminderName, 
        byte[]? data, 
        CancellationToken cancellationToken = default)
    {
        if (reminderName == "auto-cancel")
        {
            Console.WriteLine($"Order {ActorId} auto-cancelled (not confirmed in time)");
            await CancelOrderAsync(cancellationToken);
        }
    }

    private Task CancelOrderAsync(CancellationToken ct)
    {
        // Release inventory, refund payment, etc.
        return Task.CompletedTask;
    }

    private byte[] SerializeOrder(Order order) => 
        JsonSerializer.SerializeToUtf8Bytes(order);
}
```

#### Multiple Reminders

```csharp
[Actor]
public class CampaignActor : ActorBase, IRemindable
{
    private readonly IReminderTable _reminderTable;

    public CampaignActor(string actorId, IReminderTable reminderTable) 
        : base(actorId)
    {
        _reminderTable = reminderTable;
    }

    public async Task ScheduleCampaignAsync(
        DateTime startDate, 
        DateTime endDate)
    {
        // Reminder to start campaign
        await _reminderTable.RegisterReminderAsync(new Reminder
        {
            ActorType = "CampaignActor",
            ActorId = ActorId,
            ReminderName = "campaign-start",
            DueTime = startDate - DateTime.UtcNow,
            Period = Timeout.InfiniteTimeSpan,
            RegisteredAt = DateTimeOffset.UtcNow
        });

        // Reminder to end campaign
        await _reminderTable.RegisterReminderAsync(new Reminder
        {
            ActorType = "CampaignActor",
            ActorId = ActorId,
            ReminderName = "campaign-end",
            DueTime = endDate - DateTime.UtcNow,
            Period = Timeout.InfiniteTimeSpan,
            RegisteredAt = DateTimeOffset.UtcNow
        });

        // Daily progress report
        await _reminderTable.RegisterReminderAsync(new Reminder
        {
            ActorType = "CampaignActor",
            ActorId = ActorId,
            ReminderName = "daily-report",
            DueTime = GetNextMidnight() - DateTime.UtcNow,
            Period = TimeSpan.FromDays(1),
            RegisteredAt = DateTimeOffset.UtcNow
        });
    }

    public async Task ReceiveReminderAsync(
        string reminderName, 
        byte[]? data, 
        CancellationToken cancellationToken = default)
    {
        switch (reminderName)
        {
            case "campaign-start":
                Console.WriteLine($"Campaign {ActorId} starting now!");
                await StartCampaignAsync(cancellationToken);
                break;

            case "campaign-end":
                Console.WriteLine($"Campaign {ActorId} ending now!");
                await EndCampaignAsync(cancellationToken);
                break;

            case "daily-report":
                Console.WriteLine($"Generating daily report for campaign {ActorId}");
                await GenerateDailyReportAsync(cancellationToken);
                break;
        }
    }

    private DateTime GetNextMidnight()
    {
        var now = DateTime.UtcNow;
        return now.Date.AddDays(1);
    }

    private Task StartCampaignAsync(CancellationToken ct) => Task.CompletedTask;
    private Task EndCampaignAsync(CancellationToken ct) => Task.CompletedTask;
    private Task GenerateDailyReportAsync(CancellationToken ct) => Task.CompletedTask;
}
```

## Choosing Between Timers and Reminders

### Use Timers When:

✅ **Frequent callbacks** (seconds to minutes)
- Game ticks, heartbeats, polling

✅ **Transient operations** 
- Cache expiration, temporary notifications

✅ **Performance is critical**
- High-frequency updates, real-time systems

✅ **State is in-memory only**
- No need to survive restarts

### Use Reminders When:

✅ **Infrequent callbacks** (minutes to days)
- Daily reports, weekly cleanups, monthly billing

✅ **Critical operations**
- Subscription renewals, contract expirations, compliance deadlines

✅ **Must survive restarts**
- System upgrades, crashes, deployments

✅ **Actor might be deactivated**
- Reminder will re-activate the actor when it fires

## Best Practices

### 1. Timer Lifecycle Management

```csharp
public class WellBehavedActor : ActorBase
{
    private readonly List<IActorTimer> _timers = new();

    protected IActorTimer RegisterAndTrackTimer(/* params */)
    {
        var timer = TimerManager?.RegisterTimer(/* ... */);
        if (timer != null)
            _timers.Add(timer);
        return timer;
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        // Clean up all timers
        foreach (var timer in _timers)
        {
            timer.Stop();
            timer.Dispose();
        }
        _timers.Clear();

        return base.OnDeactivateAsync(cancellationToken);
    }
}
```

### 2. Reminder Error Handling

```csharp
public async Task ReceiveReminderAsync(
    string reminderName, 
    byte[]? data, 
    CancellationToken cancellationToken)
{
    try
    {
        await ProcessReminderAsync(reminderName, data, cancellationToken);
    }
    catch (Exception ex)
    {
        // Log error but don't throw - reminder will retry
        Console.WriteLine($"Reminder {reminderName} failed: {ex.Message}");
        
        // Optionally: implement exponential backoff, dead letter queue, etc.
    }
}
```

### 3. Avoiding Timer Overlaps

```csharp
[Actor]
public class ProcessorActor : ActorBase
{
    private bool _isProcessing = false;

    private async Task OnTimerTickAsync(CancellationToken ct)
    {
        if (_isProcessing)
        {
            Console.WriteLine("Previous tick still processing, skipping...");
            return;
        }

        _isProcessing = true;
        try
        {
            await DoWorkAsync(ct);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private Task DoWorkAsync(CancellationToken ct)
    {
        // Long-running work
        return Task.CompletedTask;
    }
}
```

### 4. Reminder Data Serialization

```csharp
// Use JSON for structured data
var reminderData = JsonSerializer.SerializeToUtf8Bytes(new
{
    UserId = "user-123",
    Plan = "premium",
    Amount = 9.99m
});

var reminder = new Reminder
{
    // ...
    Data = reminderData
};

// In ReceiveReminderAsync:
var payload = JsonSerializer.Deserialize<ReminderPayload>(data);
```

### 5. Testing with Timers

```csharp
// Use dependency injection for testability
public interface ITimeProvider
{
    DateTime UtcNow { get; }
}

public class ActorWithTimer : ActorBase
{
    private readonly ITimeProvider _timeProvider;

    public ActorWithTimer(string actorId, ITimeProvider timeProvider)
        : base(actorId)
    {
        _timeProvider = timeProvider;
    }

    private Task OnTimerAsync(CancellationToken ct)
    {
        var now = _timeProvider.UtcNow; // Mockable for tests
        return Task.CompletedTask;
    }
}
```

## Performance Considerations

### Timers

- **Overhead**: Very low (~100 bytes per timer)
- **Accuracy**: ±15ms on Windows, ±1ms on Linux
- **Scalability**: Thousands per actor, millions per silo
- **GC Impact**: Minimal (uses pooled timers internally)

### Reminders

- **Overhead**: High (database I/O for each operation)
- **Accuracy**: Seconds to minutes (depends on polling interval)
- **Scalability**: Hundreds per actor, thousands per silo
- **Storage**: Requires persistent storage (Redis, SQL, etc.)

## Troubleshooting

### Timer Not Firing

**Causes:**
- Timer never started: `timer.Start()` not called
- Actor deactivated: Timers stop when actor deactivates
- Exception in callback: Check logs for errors

**Solutions:**
```csharp
// Verify timer is running
var timers = TimerManager?.GetTimers();
foreach (var timer in timers)
{
    Console.WriteLine($"Timer {timer.Name}: Running={timer.IsRunning}");
}
```

### Reminder Not Firing

**Causes:**
- Reminder service not running
- Database connectivity issues
- Expired reminder (one-time already fired)

**Solutions:**
```csharp
// Check registered reminders
var reminders = await _reminderTable.GetRemindersAsync("MyActor", actorId);
Console.WriteLine($"Actor has {reminders.Count} reminders:");
foreach (var r in reminders)
{
    Console.WriteLine($"  {r.ReminderName}: Next fire at {r.NextFireTime}");
}
```

### High CPU from Timers

**Cause:** Too many timers or too-frequent callbacks

**Solutions:**
- Increase timer period if possible
- Batch operations instead of per-tick processing
- Use reminders for infrequent tasks
- Profile callback performance

## Related Topics

- **[Actor Model](Actor-Model)** - Understanding actor lifecycle
- **[Persistence](Persistence)** - Storing reminder state
- **[Clustering](Clustering)** - Reminders in distributed scenarios
- **[API Reference](API-Reference)** - Complete interface documentation
- **[FAQ](FAQ)** - Common issues and solutions

---

**Next Steps:**
- Explore [Streaming](Streaming) for reactive event processing
- Learn about [Clustering](Clustering) for distributed actors
- Check [Examples](Examples) for more patterns
