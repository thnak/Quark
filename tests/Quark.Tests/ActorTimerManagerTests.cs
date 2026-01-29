using Quark.Core.Timers;

namespace Quark.Tests;

public class ActorTimerManagerTests
{
    [Fact]
    public void RegisterTimer_CreatesAndStartsTimer()
    {
        // Arrange
        var manager = new ActorTimerManager();

        // Act
        var timer = manager.RegisterTimer(
            "timer1",
            TimeSpan.FromMilliseconds(50),
            null,
            () => Task.CompletedTask);

        // Assert
        Assert.NotNull(timer);
        Assert.Equal("timer1", timer.Name);
        Assert.True(timer.IsRunning);
    }

    [Fact]
    public async Task RegisterTimer_CallbackInvoked()
    {
        // Arrange
        var manager = new ActorTimerManager();
        var tcs = new TaskCompletionSource<bool>();

        // Act
        manager.RegisterTimer(
            "timer1",
            TimeSpan.FromMilliseconds(50),
            null,
            async () =>
            {
                tcs.SetResult(true);
                await Task.CompletedTask;
            });

        // Assert
        var result = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        Assert.Same(tcs.Task, result);
        Assert.True(await tcs.Task);
    }

    [Fact]
    public async Task RegisterTimer_RecurringTimer_CallbackInvokedMultipleTimes()
    {
        // Arrange
        var manager = new ActorTimerManager();
        var callCount = 0;

        // Act
        manager.RegisterTimer(
            "timer1",
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50),
            async () =>
            {
                Interlocked.Increment(ref callCount);
                await Task.CompletedTask;
            });

        await Task.Delay(200);

        // Assert
        Assert.True(callCount >= 2, $"Expected at least 2 invocations, got {callCount}");
    }

    [Fact]
    public void RegisterTimer_DuplicateName_ThrowsArgumentException()
    {
        // Arrange
        var manager = new ActorTimerManager();
        manager.RegisterTimer("timer1", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            manager.RegisterTimer("timer1", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask));

        Assert.Contains("timer1", ex.Message);
    }

    [Fact]
    public void RegisterTimer_NullOrWhitespaceName_ThrowsArgumentException()
    {
        // Arrange
        var manager = new ActorTimerManager();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            manager.RegisterTimer(null!, TimeSpan.FromSeconds(10), null, () => Task.CompletedTask));

        Assert.Throws<ArgumentException>(() =>
            manager.RegisterTimer("", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask));

        Assert.Throws<ArgumentException>(() =>
            manager.RegisterTimer("   ", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask));
    }

    [Fact]
    public void UnregisterTimer_RemovesTimer()
    {
        // Arrange
        var manager = new ActorTimerManager();
        manager.RegisterTimer("timer1", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask);

        // Act
        var result = manager.UnregisterTimer("timer1");
        var timer = manager.GetTimer("timer1");

        // Assert
        Assert.True(result);
        Assert.Null(timer);
    }

    [Fact]
    public void UnregisterTimer_NonExistentTimer_ReturnsFalse()
    {
        // Arrange
        var manager = new ActorTimerManager();

        // Act
        var result = manager.UnregisterTimer("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetTimer_ExistingTimer_ReturnsTimer()
    {
        // Arrange
        var manager = new ActorTimerManager();
        var registered = manager.RegisterTimer("timer1", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask);

        // Act
        var timer = manager.GetTimer("timer1");

        // Assert
        Assert.NotNull(timer);
        Assert.Same(registered, timer);
    }

    [Fact]
    public void GetTimer_NonExistentTimer_ReturnsNull()
    {
        // Arrange
        var manager = new ActorTimerManager();

        // Act
        var timer = manager.GetTimer("nonexistent");

        // Assert
        Assert.Null(timer);
    }

    [Fact]
    public void GetAllTimers_ReturnsAllRegisteredTimers()
    {
        // Arrange
        var manager = new ActorTimerManager();
        manager.RegisterTimer("timer1", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask);
        manager.RegisterTimer("timer2", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask);
        manager.RegisterTimer("timer3", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask);

        // Act
        var timers = manager.GetAllTimers();

        // Assert
        Assert.Equal(3, timers.Count);
        Assert.Contains(timers, t => t.Name == "timer1");
        Assert.Contains(timers, t => t.Name == "timer2");
        Assert.Contains(timers, t => t.Name == "timer3");
    }

    [Fact]
    public void Dispose_DisposesAllTimers()
    {
        // Arrange
        var manager = new ActorTimerManager();
        var timer1 = manager.RegisterTimer("timer1", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask);
        var timer2 = manager.RegisterTimer("timer2", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask);

        // Act
        manager.Dispose();

        // Assert
        Assert.False(timer1.IsRunning);
        Assert.False(timer2.IsRunning);
    }

    [Fact]
    public void AfterDispose_OperationsThrow()
    {
        // Arrange
        var manager = new ActorTimerManager();
        manager.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            manager.RegisterTimer("timer1", TimeSpan.FromSeconds(10), null, () => Task.CompletedTask));

        Assert.Throws<ObjectDisposedException>(() =>
            manager.UnregisterTimer("timer1"));

        Assert.Throws<ObjectDisposedException>(() =>
            manager.GetTimer("timer1"));

        Assert.Throws<ObjectDisposedException>(() =>
            manager.GetAllTimers());
    }

    [Fact]
    public void TimerStop_StopsCallback()
    {
        // Arrange
        var manager = new ActorTimerManager();
        var callCount = 0;

        var timer = manager.RegisterTimer(
            "timer1",
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50),
            async () =>
            {
                Interlocked.Increment(ref callCount);
                await Task.CompletedTask;
            });

        // Act
        timer.Stop();

        // Assert
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public async Task TimerStart_AfterStop_RestartsTimer()
    {
        // Arrange
        var manager = new ActorTimerManager();
        var tcs = new TaskCompletionSource<bool>();

        var timer = manager.RegisterTimer(
            "timer1",
            TimeSpan.FromMilliseconds(50),
            null,
            async () =>
            {
                tcs.TrySetResult(true);
                await Task.CompletedTask;
            });

        timer.Stop();

        // Act
        timer.Start();

        // Assert
        Assert.True(timer.IsRunning);
        var result = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        Assert.Same(tcs.Task, result);
    }
}
