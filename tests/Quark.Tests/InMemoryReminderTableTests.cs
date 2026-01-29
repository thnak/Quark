using Quark.Abstractions.Reminders;
using Quark.Core.Reminders;

namespace Quark.Tests;

public class InMemoryReminderTableTests
{
    [Fact]
    public async Task RegisterAsync_StoresReminder()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var reminder = new Reminder(
            "actor1",
            "TestActor",
            "reminder1",
            DateTimeOffset.UtcNow.AddMinutes(5),
            TimeSpan.FromMinutes(10));

        // Act
        await table.RegisterAsync(reminder);
        var reminders = await table.GetRemindersAsync("actor1");

        // Assert
        Assert.Single(reminders);
        Assert.Equal("reminder1", reminders[0].Name);
        Assert.Equal("actor1", reminders[0].ActorId);
    }

    [Fact]
    public async Task RegisterAsync_UpdatesExistingReminder()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var reminder1 = new Reminder(
            "actor1",
            "TestActor",
            "reminder1",
            DateTimeOffset.UtcNow.AddMinutes(5),
            TimeSpan.FromMinutes(10));

        var reminder2 = new Reminder(
            "actor1",
            "TestActor",
            "reminder1",
            DateTimeOffset.UtcNow.AddMinutes(15),
            TimeSpan.FromMinutes(20));

        // Act
        await table.RegisterAsync(reminder1);
        await table.RegisterAsync(reminder2);
        var reminders = await table.GetRemindersAsync("actor1");

        // Assert
        Assert.Single(reminders);
        Assert.Equal(TimeSpan.FromMinutes(20), reminders[0].Period);
    }

    [Fact]
    public async Task UnregisterAsync_RemovesReminder()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var reminder = new Reminder(
            "actor1",
            "TestActor",
            "reminder1",
            DateTimeOffset.UtcNow.AddMinutes(5),
            TimeSpan.FromMinutes(10));

        // Act
        await table.RegisterAsync(reminder);
        await table.UnregisterAsync("actor1", "reminder1");
        var reminders = await table.GetRemindersAsync("actor1");

        // Assert
        Assert.Empty(reminders);
    }

    [Fact]
    public async Task GetRemindersAsync_ReturnsOnlyActorReminders()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var reminder1 = new Reminder("actor1", "TestActor", "reminder1", DateTimeOffset.UtcNow.AddMinutes(5));
        var reminder2 = new Reminder("actor2", "TestActor", "reminder2", DateTimeOffset.UtcNow.AddMinutes(5));
        var reminder3 = new Reminder("actor1", "TestActor", "reminder3", DateTimeOffset.UtcNow.AddMinutes(5));

        // Act
        await table.RegisterAsync(reminder1);
        await table.RegisterAsync(reminder2);
        await table.RegisterAsync(reminder3);
        var reminders = await table.GetRemindersAsync("actor1");

        // Assert
        Assert.Equal(2, reminders.Count);
        Assert.All(reminders, r => Assert.Equal("actor1", r.ActorId));
    }

    [Fact]
    public async Task GetDueRemindersForSiloAsync_ReturnsDueReminders()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var now = DateTimeOffset.UtcNow;

        var dueReminder = new Reminder("actor1", "TestActor", "due", now.AddSeconds(-1));
        var futureReminder = new Reminder("actor2", "TestActor", "future", now.AddMinutes(5));

        // Act
        await table.RegisterAsync(dueReminder);
        await table.RegisterAsync(futureReminder);
        var reminders = await table.GetDueRemindersForSiloAsync("silo1", now);

        // Assert
        Assert.Single(reminders);
        Assert.Equal("due", reminders[0].Name);
    }

    [Fact]
    public async Task GetDueRemindersForSiloAsync_WithNullHashRing_ReturnsAllDueReminders()
    {
        // Arrange
        var table = new InMemoryReminderTable(hashRing: null);
        var now = DateTimeOffset.UtcNow;

        var reminder1 = new Reminder("actor1", "TestActor", "reminder1", now.AddSeconds(-1));
        var reminder2 = new Reminder("actor2", "TestActor", "reminder2", now.AddSeconds(-1));

        // Act
        await table.RegisterAsync(reminder1);
        await table.RegisterAsync(reminder2);
        var reminders = await table.GetDueRemindersForSiloAsync("silo1", now);

        // Assert
        Assert.Equal(2, reminders.Count);
    }

    [Fact]
    public async Task UpdateFireTimeAsync_UpdatesReminderTimes()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder("actor1", "TestActor", "reminder1", now);

        // Act
        await table.RegisterAsync(reminder);
        var newLastFired = now.AddMinutes(1);
        var newNextFire = now.AddMinutes(11);
        await table.UpdateFireTimeAsync("actor1", "reminder1", newLastFired, newNextFire);

        var reminders = await table.GetRemindersAsync("actor1");

        // Assert
        Assert.Single(reminders);
        Assert.Equal(newLastFired, reminders[0].LastFiredAt);
        Assert.Equal(newNextFire, reminders[0].NextFireTime);
    }

    [Fact]
    public async Task UpdateFireTimeAsync_NonExistentReminder_DoesNotThrow()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var now = DateTimeOffset.UtcNow;

        // Act & Assert
        await table.UpdateFireTimeAsync("actor1", "nonexistent", now, now.AddMinutes(10));
    }

    [Fact]
    public async Task MultipleReminders_SameActor_AllStored()
    {
        // Arrange
        var table = new InMemoryReminderTable();
        var reminder1 = new Reminder("actor1", "TestActor", "reminder1", DateTimeOffset.UtcNow.AddMinutes(5));
        var reminder2 = new Reminder("actor1", "TestActor", "reminder2", DateTimeOffset.UtcNow.AddMinutes(10));
        var reminder3 = new Reminder("actor1", "TestActor", "reminder3", DateTimeOffset.UtcNow.AddMinutes(15));

        // Act
        await table.RegisterAsync(reminder1);
        await table.RegisterAsync(reminder2);
        await table.RegisterAsync(reminder3);
        var reminders = await table.GetRemindersAsync("actor1");

        // Assert
        Assert.Equal(3, reminders.Count);
    }
}
