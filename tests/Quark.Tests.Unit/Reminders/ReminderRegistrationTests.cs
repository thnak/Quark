using Microsoft.Extensions.DependencyInjection;
using Quark.Reminders.InMemory;
using Quark.Reminders.Redis;
using Xunit;

namespace Quark.Tests.Unit.Reminders;

public sealed class ReminderRegistrationTests
{
    [Fact]
    public void AddInMemoryReminders_ThenAddRedisReminders_Throws()
    {
        var services = new ServiceCollection();
        services.AddInMemoryReminders();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddRedisReminders());
        Assert.Contains("already configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddRedisReminders_ThenAddInMemoryReminders_Throws()
    {
        var services = new ServiceCollection();
        services.AddRedisReminders();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddInMemoryReminders());
        Assert.Contains("already configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddInMemoryReminders_CalledTwice_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddInMemoryReminders();

        var ex = Record.Exception(() => services.AddInMemoryReminders());
        Assert.Null(ex);
    }

    [Fact]
    public void AddRedisReminders_CalledTwice_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddRedisReminders();

        var ex = Record.Exception(() => services.AddRedisReminders());
        Assert.Null(ex);
    }
}
