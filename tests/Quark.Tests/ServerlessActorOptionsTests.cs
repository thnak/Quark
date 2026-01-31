// Copyright (c) Quark Framework. All rights reserved.

using Quark.Abstractions;
using Xunit;

namespace Quark.Tests;

public class ServerlessActorOptionsTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var options = new ServerlessActorOptions();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), options.IdleTimeout);
        Assert.Equal(TimeSpan.FromMinutes(1), options.CheckInterval);
        Assert.False(options.Enabled);
        Assert.Equal(0, options.MinimumActiveActors);
        Assert.False(options.EagerStateLoading);
    }

    [Fact]
    public void IdleTimeout_CanBeSet()
    {
        // Arrange
        var options = new ServerlessActorOptions();
        var newTimeout = TimeSpan.FromMinutes(10);

        // Act
        options.IdleTimeout = newTimeout;

        // Assert
        Assert.Equal(newTimeout, options.IdleTimeout);
    }

    [Fact]
    public void CheckInterval_CanBeSet()
    {
        // Arrange
        var options = new ServerlessActorOptions();
        var newInterval = TimeSpan.FromSeconds(30);

        // Act
        options.CheckInterval = newInterval;

        // Assert
        Assert.Equal(newInterval, options.CheckInterval);
    }

    [Fact]
    public void Enabled_CanBeSet()
    {
        // Arrange
        var options = new ServerlessActorOptions();

        // Act
        options.Enabled = true;

        // Assert
        Assert.True(options.Enabled);
    }

    [Fact]
    public void MinimumActiveActors_CanBeSet()
    {
        // Arrange
        var options = new ServerlessActorOptions();

        // Act
        options.MinimumActiveActors = 5;

        // Assert
        Assert.Equal(5, options.MinimumActiveActors);
    }

    [Fact]
    public void EagerStateLoading_CanBeSet()
    {
        // Arrange
        var options = new ServerlessActorOptions();

        // Act
        options.EagerStateLoading = true;

        // Assert
        Assert.True(options.EagerStateLoading);
    }
}
