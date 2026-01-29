using Microsoft.Extensions.Logging.Abstractions;
using Quark.Abstractions.Reminders;
using Quark.Core.Reminders;

namespace Quark.Tests;

public class ReminderTickManagerTests
{
    [Fact]
    public async Task ReminderFired_EventRaised_WhenReminderIsDue()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder("actor1", "TestActor", "reminder1", now.AddSeconds(-1));
        await table.RegisterAsync(reminder);

        var manager = new ReminderTickManager(
            table,
            "silo1",
            NullLogger<ReminderTickManager>.Instance,
            TimeSpan.FromMilliseconds(50));

        ReminderFiredEventArgs? firedEvent = null;
        manager.ReminderFired += (sender, args) => firedEvent = args;

        using var cts = new CancellationTokenSource();

        // Act
        var task = manager.StartAsync(cts.Token);
        await Task.Delay(150); // Wait for at least 2 ticks
        await cts.CancelAsync();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        Assert.NotNull(firedEvent);
        Assert.Equal("reminder1", firedEvent.Reminder.Name);
        Assert.Equal("actor1", firedEvent.Reminder.ActorId);
    }

    [Fact]
    public async Task RecurringReminder_UpdatesNextFireTime()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder(
            "actor1",
            "TestActor",
            "reminder1",
            now.AddSeconds(-1),
            TimeSpan.FromMinutes(10));
        await table.RegisterAsync(reminder);

        var manager = new ReminderTickManager(
            table,
            "silo1",
            NullLogger<ReminderTickManager>.Instance,
            TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource();

        // Act
        var task = manager.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        var reminders = await table.GetRemindersAsync("actor1");

        // Assert
        Assert.Single(reminders);
        Assert.NotNull(reminders[0].LastFiredAt);
        Assert.True(reminders[0].NextFireTime > now);
    }

    [Fact]
    public async Task OneTimeReminder_UnregisteredAfterFiring()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder("actor1", "TestActor", "reminder1", now.AddSeconds(-1), period: null);
        await table.RegisterAsync(reminder);

        var manager = new ReminderTickManager(
            table,
            "silo1",
            NullLogger<ReminderTickManager>.Instance,
            TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource();

        // Act
        var task = manager.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        var reminders = await table.GetRemindersAsync("actor1");

        // Assert
        Assert.Empty(reminders);
    }

    [Fact]
    public async Task FutureReminder_NotFired()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder("actor1", "TestActor", "reminder1", now.AddMinutes(10));
        await table.RegisterAsync(reminder);

        var manager = new ReminderTickManager(
            table,
            "silo1",
            NullLogger<ReminderTickManager>.Instance,
            TimeSpan.FromMilliseconds(50));

        ReminderFiredEventArgs? firedEvent = null;
        manager.ReminderFired += (sender, args) => firedEvent = args;

        using var cts = new CancellationTokenSource();

        // Act
        var task = manager.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        Assert.Null(firedEvent);
    }

    [Fact]
    public async Task MultipleReminders_AllFired()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var now = DateTimeOffset.UtcNow;
        var reminder1 = new Reminder("actor1", "TestActor", "reminder1", now.AddSeconds(-1));
        var reminder2 = new Reminder("actor2", "TestActor", "reminder2", now.AddSeconds(-1));
        await table.RegisterAsync(reminder1);
        await table.RegisterAsync(reminder2);

        var manager = new ReminderTickManager(
            table,
            "silo1",
            NullLogger<ReminderTickManager>.Instance,
            TimeSpan.FromMilliseconds(50));

        var firedReminders = new System.Collections.Concurrent.ConcurrentBag<string>();
        manager.ReminderFired += (sender, args) => firedReminders.Add(args.Reminder.Name);

        using var cts = new CancellationTokenSource();

        // Act
        var task = manager.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        Assert.Equal(2, firedReminders.Count);
        Assert.Contains("reminder1", firedReminders);
        Assert.Contains("reminder2", firedReminders);
    }
}
