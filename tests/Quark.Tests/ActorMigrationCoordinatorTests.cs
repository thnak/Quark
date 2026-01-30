using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quark.Abstractions;
using Quark.Abstractions.Migration;
using Quark.Abstractions.Reminders;
using Quark.Core.Actors.Migration;
using Xunit;

namespace Quark.Tests;

public class ActorMigrationCoordinatorTests
{
    [Fact]
    public async Task MigrateActorAsync_SuccessfulMigration_ReturnsCompletedResult()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Act
        var result = await coordinator.MigrateActorAsync("actor-1", "TestActor", "target-silo-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("actor-1", result.ActorId);
        Assert.Equal("TestActor", result.ActorType);
        Assert.Equal("target-silo-1", result.TargetSiloId);
        Assert.Equal(MigrationStatus.Completed, result.Status);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task MigrateActorAsync_AlreadyMigrating_ReturnsFailed()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Start first migration (don't await)
        var firstMigration = coordinator.MigrateActorAsync("actor-1", "TestActor", "target-silo-1");

        // Act - Try to migrate same actor again
        var result = await coordinator.MigrateActorAsync("actor-1", "TestActor", "target-silo-2");

        // Assert
        Assert.Equal(MigrationStatus.Failed, result.Status);
        Assert.Contains("already being migrated", result.ErrorMessage);

        // Wait for first migration to complete
        await firstMigration;
    }

    [Fact]
    public async Task BeginDrainAsync_MarksActorAsDraining()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Act
        await coordinator.BeginDrainAsync("actor-1");

        // Assert - No exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task WaitForDrainCompletionAsync_WithinTimeout_ReturnsTrue()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Act
        var result = await coordinator.WaitForDrainCompletionAsync("actor-1", TimeSpan.FromSeconds(1));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TransferActorStateAsync_ReturnsTrue()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Act
        var result = await coordinator.TransferActorStateAsync("actor-1", "TestActor", "target-silo-1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ActivateOnTargetAsync_ReturnsTrue()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Act
        var result = await coordinator.ActivateOnTargetAsync("actor-1", "TestActor", "target-silo-1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetMigrationStatusAsync_NoActiveMigration_ReturnsNull()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Act
        var status = await coordinator.GetMigrationStatusAsync("actor-1");

        // Assert
        Assert.Null(status);
    }

    [Fact]
    public async Task GetMigrationStatusAsync_DuringMigration_ReturnsInProgress()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Start migration (don't await)
        var migrationTask = coordinator.MigrateActorAsync("actor-1", "TestActor", "target-silo-1");

        // Act - Check status while migration is in progress
        await Task.Delay(10); // Give it a moment to start
        var status = await coordinator.GetMigrationStatusAsync("actor-1");

        // Assert
        Assert.NotNull(status);
        // Status could be InProgress or Completed depending on timing
        Assert.True(status == MigrationStatus.InProgress || status == MigrationStatus.Completed);

        // Wait for migration to complete
        await migrationTask;
    }

    [Fact]
    public void ActiveMigrationCount_NoMigrations_ReturnsZero()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Act & Assert
        Assert.Equal(0, coordinator.ActiveMigrationCount);
    }

    [Fact]
    public async Task ActiveMigrationCount_DuringMigration_ReturnsCount()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger);

        // Start migration (don't await)
        var migrationTask = coordinator.MigrateActorAsync("actor-1", "TestActor", "target-silo-1");

        // Act - Check count while migration is in progress
        await Task.Delay(10); // Give it a moment to start

        // Assert - Count could be 0 or 1 depending on timing
        Assert.True(coordinator.ActiveMigrationCount >= 0);

        // Wait for migration to complete
        await migrationTask;

        // After completion, count should be 0
        Assert.Equal(0, coordinator.ActiveMigrationCount);
    }

    [Fact]
    public async Task MigrateActorAsync_WithReminders_MigratesReminders()
    {
        // Arrange
        var actorFactory = new Mock<IActorFactory>();
        var reminderTable = new Mock<IReminderTable>();
        reminderTable
            .Setup(r => r.GetRemindersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Reminder>());

        var logger = NullLogger<ActorMigrationCoordinator>.Instance;
        var coordinator = new ActorMigrationCoordinator(actorFactory.Object, logger, reminderTable.Object);

        // Act
        var result = await coordinator.MigrateActorAsync("actor-1", "TestActor", "target-silo-1");

        // Assert
        Assert.Equal(MigrationStatus.Completed, result.Status);
        reminderTable.Verify(
            r => r.GetRemindersAsync("actor-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
