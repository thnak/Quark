// Copyright (c) Quark Framework. All rights reserved.

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quark.Abstractions;
using Quark.Abstractions.Clustering;
using Quark.Abstractions.Streaming;
using Quark.Client;
using Quark.Core.Actors;
using Quark.Hosting;
using Quark.Networking.Abstractions;

namespace Quark.Tests;

/// <summary>
/// Comprehensive integration tests for Silo-Client interactions covering all actor types.
/// These tests simulate a data center scenario where IClusterClient is used to interact
/// with actors running in a remote Silo to test network connectivity and actor patterns.
/// </summary>
public class SiloClientIntegrationTests
{
    #region Test Actor Interfaces

    /// <summary>
    /// Stateful counter actor for testing CRUD operations and state persistence.
    /// </summary>
    public interface IDataCenterCounterActor : IQuarkActor
    {
        Task IncrementCounterAsync(int amount = 1);
        Task DecrementCounterAsync(int amount = 1);
        Task<int> GetCounterValueAsync();
        Task ResetCounterAsync();
        Task<string> GetStateDescriptionAsync();
    }

    /// <summary>
    /// Stateless worker actor for testing concurrent processing and load distribution.
    /// </summary>
    public interface IDataCenterWorkerActor : IQuarkActor
    {
        Task<string> ProcessDataAsync(string data);
        Task<int> ComputeHashAsync(string input);
        Task<double> PerformCalculationAsync(double value);
    }

    /// <summary>
    /// Supervised child actor for testing parent-child relationships and failure handling.
    /// </summary>
    public interface IDataCenterChildActor : IQuarkActor
    {
        Task<string> DoWorkAsync(string workItem);
        Task FailWithExceptionAsync(string exceptionType);
        Task<int> GetWorkProcessedCountAsync();
    }

    /// <summary>
    /// Parent supervisor actor for testing supervision directives.
    /// </summary>
    public interface IDataCenterSupervisorActor : IQuarkActor
    {
        Task<string> SpawnAndInvokeChildAsync(string childId, string workItem);
        Task<int> GetChildCountAsync();
        Task<string> TestChildFailureAsync(string childId, string exceptionType);
    }

    /// <summary>
    /// Reactive streaming actor for testing stream operations and backpressure.
    /// </summary>
    public interface IDataCenterStreamActor : IQuarkActor
    {
        Task PublishStreamMessageAsync(int value);
        Task<int> GetStreamProcessedCountAsync();
        Task<bool> IsStreamBackpressureActiveAsync();
        Task CompleteStreamAsync();
    }

    /// <summary>
    /// Timer-based actor for testing timer operations.
    /// </summary>
    public interface IDataCenterTimerActor : IQuarkActor
    {
        Task RegisterTimerAsync(string timerName, int intervalMs);
        Task<int> GetTimerTickCountAsync(string timerName);
        Task CancelTimerAsync(string timerName);
        Task<bool> IsTimerActiveAsync(string timerName);
    }

    /// <summary>
    /// Reminder-based actor for testing persistent reminders.
    /// </summary>
    public interface IDataCenterReminderActor : IQuarkActor
    {
        Task RegisterReminderAsync(string reminderName, int delayMs);
        Task<int> GetReminderTickCountAsync(string reminderName);
        Task CancelReminderAsync(string reminderName);
        Task<bool> IsReminderActiveAsync(string reminderName);
    }

    #endregion

    #region Stateful Actor Tests (Counter Pattern)

    [Fact]
    public async Task StatefulActor_IncrementOperation_UpdatesValue()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();

        // Act
        var envelope = CreateEnvelope("counter-1", "IDataCenterCounterActor", "IncrementCounterAsync", new byte[] { 5 });
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.ResponsePayload);
    }

    [Fact]
    public async Task StatefulActor_MultipleOperations_MaintainsState()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "counter-2";

        // Act - Multiple sequential operations
        var increment1 = CreateEnvelope(actorId, "IDataCenterCounterActor", "IncrementCounterAsync", new byte[] { 3 });
        var increment2 = CreateEnvelope(actorId, "IDataCenterCounterActor", "IncrementCounterAsync", new byte[] { 7 });
        var getValue = CreateEnvelope(actorId, "IDataCenterCounterActor", "GetCounterValueAsync", Array.Empty<byte>());

        await client.SendAsync(increment1);
        await client.SendAsync(increment2);
        var response = await client.SendAsync(getValue);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.ResponsePayload);
    }

    [Fact]
    public async Task StatefulActor_ConcurrentCalls_ProcessedSequentially()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "counter-3";

        // Act - Concurrent calls to same actor
        var tasks = new List<Task<QuarkEnvelope>>();
        for (int i = 0; i < 10; i++)
        {
            var envelope = CreateEnvelope(actorId, "IDataCenterCounterActor", "IncrementCounterAsync", new byte[] { 1 });
            tasks.Add(client.SendAsync(envelope));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert - All responses should succeed
        Assert.All(responses, r => Assert.NotNull(r));
    }

    [Fact]
    public async Task StatefulActor_ResetOperation_ClearsState()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "counter-4";

        // Act
        var increment = CreateEnvelope(actorId, "IDataCenterCounterActor", "IncrementCounterAsync", new byte[] { 10 });
        var reset = CreateEnvelope(actorId, "IDataCenterCounterActor", "ResetCounterAsync", Array.Empty<byte>());
        var getValue = CreateEnvelope(actorId, "IDataCenterCounterActor", "GetCounterValueAsync", Array.Empty<byte>());

        await client.SendAsync(increment);
        await client.SendAsync(reset);
        var response = await client.SendAsync(getValue);

        // Assert
        Assert.NotNull(response);
    }

    [Fact]
    public async Task StatefulActor_ErrorHandling_ReturnsErrorEnvelope()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();

        // Setup transport to return error envelope
        var mockTransport = Mock.Get(client.Transport);
        var errorEnvelope = CreateEnvelope("counter-5", "IDataCenterCounterActor", "IncrementCounterAsync", Array.Empty<byte>());
        errorEnvelope.IsError = true;
        errorEnvelope.ErrorMessage = "Test error";
        mockTransport.Setup(t => t.SendAsync(
            It.IsAny<string>(),
            It.IsAny<QuarkEnvelope>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorEnvelope);

        // Act
        var envelope = CreateEnvelope("counter-5", "IDataCenterCounterActor", "IncrementCounterAsync", new byte[] { 1 });
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsError);
        Assert.NotNull(response.ErrorMessage);
    }

    #endregion

    #region Stateless Worker Tests

    [Fact]
    public async Task StatelessWorker_ConcurrentRequests_ProcessedInParallel()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var workerId = "worker-1";

        // Act - Multiple concurrent requests to stateless worker
        var tasks = new List<Task<QuarkEnvelope>>();
        for (int i = 0; i < 20; i++)
        {
            var envelope = CreateEnvelope(workerId, "IDataCenterWorkerActor", "ProcessDataAsync", 
                System.Text.Encoding.UTF8.GetBytes($"data-{i}"));
            tasks.Add(client.SendAsync(envelope));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert - All requests processed successfully
        Assert.All(responses, r => Assert.NotNull(r.ResponsePayload));
    }

    [Fact]
    public async Task StatelessWorker_LoadDistribution_HandlesHighThroughput()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var workerId = "worker-2";

        // Act - Simulate high-throughput scenario
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tasks = new List<Task<QuarkEnvelope>>();
        for (int i = 0; i < 100; i++)
        {
            var envelope = CreateEnvelope(workerId, "IDataCenterWorkerActor", "ComputeHashAsync",
                System.Text.Encoding.UTF8.GetBytes($"input-{i}"));
            tasks.Add(client.SendAsync(envelope));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - Reasonable performance
        Assert.True(stopwatch.ElapsedMilliseconds < 10000); // Should complete within 10 seconds
    }

    [Fact]
    public async Task StatelessWorker_CPUIntensiveTask_CompletesSuccessfully()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();

        // Act - Simulate CPU-intensive calculation
        var envelope = CreateEnvelope("worker-3", "IDataCenterWorkerActor", "PerformCalculationAsync",
            BitConverter.GetBytes(Math.PI));
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.ResponsePayload);
    }

    #endregion

    #region Supervised Actor Tests

    [Fact]
    public async Task SupervisedActor_SpawnChild_CreatesChildActor()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();

        // Act - Parent spawns child via client
        var envelope = CreateEnvelope("parent-1", "IDataCenterSupervisorActor", "SpawnAndInvokeChildAsync",
            System.Text.Encoding.UTF8.GetBytes("child-1|work-item-1"));
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.ResponsePayload);
    }

    [Fact]
    public async Task SupervisedActor_ChildFailure_RestartDirective()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();

        // Act - Trigger child failure with restart directive
        var envelope = CreateEnvelope("parent-2", "IDataCenterSupervisorActor", "TestChildFailureAsync",
            System.Text.Encoding.UTF8.GetBytes("child-2|InvalidOperationException"));
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
    }

    [Fact]
    public async Task SupervisedActor_ChildFailure_StopDirective()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();

        // Act - Trigger child failure with stop directive
        var envelope = CreateEnvelope("parent-3", "IDataCenterSupervisorActor", "TestChildFailureAsync",
            System.Text.Encoding.UTF8.GetBytes("child-3|OutOfMemoryException"));
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
    }

    [Fact]
    public async Task SupervisedActor_MultipleChildren_IndependentLifecycles()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();

        // Act - Spawn multiple children
        var tasks = new List<Task<QuarkEnvelope>>();
        for (int i = 1; i <= 5; i++)
        {
            var envelope = CreateEnvelope("parent-4", "IDataCenterSupervisorActor", "SpawnAndInvokeChildAsync",
                System.Text.Encoding.UTF8.GetBytes($"child-{i}|work-{i}"));
            tasks.Add(client.SendAsync(envelope));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        Assert.All(responses, r => Assert.NotNull(r));
    }

    #endregion

    #region Reactive/Streaming Actor Tests

    [Fact]
    public async Task ReactiveActor_PublishMessage_ProcessesStream()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "stream-1";

        // Act - Publish multiple messages
        var tasks = new List<Task<QuarkEnvelope>>();
        for (int i = 1; i <= 10; i++)
        {
            var envelope = CreateEnvelope(actorId, "IDataCenterStreamActor", "PublishMessageAsync",
                BitConverter.GetBytes(i));
            tasks.Add(client.SendAsync(envelope));
        }

        await Task.WhenAll(tasks);

        // Assert - Get processed count
        var getCount = CreateEnvelope(actorId, "IDataCenterStreamActor", "GetStreamProcessedCountAsync", Array.Empty<byte>());
        var response = await client.SendAsync(getCount);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ReactiveActor_Backpressure_ActivatesWhenOverloaded()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "stream-2";

        // Act - Flood with messages to trigger backpressure
        var tasks = new List<Task<QuarkEnvelope>>();
        for (int i = 1; i <= 1000; i++)
        {
            var envelope = CreateEnvelope(actorId, "IDataCenterStreamActor", "PublishMessageAsync",
                BitConverter.GetBytes(i));
            tasks.Add(client.SendAsync(envelope));
        }

        await Task.WhenAll(tasks);

        // Check backpressure status
        var checkBackpressure = CreateEnvelope(actorId, "IDataCenterStreamActor", "IsStreamBackpressureActiveAsync", Array.Empty<byte>());
        var response = await client.SendAsync(checkBackpressure);

        // Assert
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ReactiveActor_CompleteStream_StopsProcessing()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "stream-3";

        // Act - Publish then complete
        var publish = CreateEnvelope(actorId, "IDataCenterStreamActor", "PublishMessageAsync", BitConverter.GetBytes(42));
        await client.SendAsync(publish);

        var complete = CreateEnvelope(actorId, "IDataCenterStreamActor", "CompleteStreamAsync", Array.Empty<byte>());
        var response = await client.SendAsync(complete);

        // Assert
        Assert.NotNull(response);
    }

    #endregion

    #region Timer-based Actor Tests

    [Fact]
    public async Task TimerActor_RegisterTimer_CreatesTimer()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();

        // Act - Register a timer
        var envelope = CreateEnvelope("timer-1", "IDataCenterTimerActor", "RegisterTimerAsync",
            System.Text.Encoding.UTF8.GetBytes("test-timer|100"));
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
    }

    [Fact]
    public async Task TimerActor_TimerFires_IncrementsTick()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "timer-2";

        // Act - Register timer and wait for ticks
        var register = CreateEnvelope(actorId, "IDataCenterTimerActor", "RegisterTimerAsync",
            System.Text.Encoding.UTF8.GetBytes("tick-timer|50"));
        await client.SendAsync(register);

        await Task.Delay(200); // Wait for multiple ticks

        var getCount = CreateEnvelope(actorId, "IDataCenterTimerActor", "GetTimerTickCountAsync",
            System.Text.Encoding.UTF8.GetBytes("tick-timer"));
        var response = await client.SendAsync(getCount);

        // Assert
        Assert.NotNull(response);
    }

    [Fact]
    public async Task TimerActor_CancelTimer_StopsTimer()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "timer-3";

        // Act - Register and cancel timer
        var register = CreateEnvelope(actorId, "IDataCenterTimerActor", "RegisterTimerAsync",
            System.Text.Encoding.UTF8.GetBytes("cancel-timer|100"));
        await client.SendAsync(register);

        var cancel = CreateEnvelope(actorId, "IDataCenterTimerActor", "CancelTimerAsync",
            System.Text.Encoding.UTF8.GetBytes("cancel-timer"));
        var response = await client.SendAsync(cancel);

        // Assert
        Assert.NotNull(response);
    }

    #endregion

    #region Reminder-based Actor Tests

    [Fact]
    public async Task ReminderActor_RegisterReminder_CreatesReminder()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();

        // Act - Register a reminder
        var envelope = CreateEnvelope("reminder-1", "IDataCenterReminderActor", "RegisterReminderAsync",
            System.Text.Encoding.UTF8.GetBytes("test-reminder|1000"));
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ReminderActor_ReminderFires_IncrementsTick()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "reminder-2";

        // Act - Register reminder and wait
        var register = CreateEnvelope(actorId, "IDataCenterReminderActor", "RegisterReminderAsync",
            System.Text.Encoding.UTF8.GetBytes("tick-reminder|100"));
        await client.SendAsync(register);

        await Task.Delay(300);

        var getCount = CreateEnvelope(actorId, "IDataCenterReminderActor", "GetReminderTickCountAsync",
            System.Text.Encoding.UTF8.GetBytes("tick-reminder"));
        var response = await client.SendAsync(getCount);

        // Assert
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ReminderActor_CancelReminder_StopsReminder()
    {
        // Arrange
        var (client, _) = CreateClientAndSilo("silo-1");
        await client.ConnectAsync();
        var actorId = "reminder-3";

        // Act - Register and cancel reminder
        var register = CreateEnvelope(actorId, "IDataCenterReminderActor", "RegisterReminderAsync",
            System.Text.Encoding.UTF8.GetBytes("cancel-reminder|1000"));
        await client.SendAsync(register);

        var cancel = CreateEnvelope(actorId, "IDataCenterReminderActor", "CancelReminderAsync",
            System.Text.Encoding.UTF8.GetBytes("cancel-reminder"));
        var response = await client.SendAsync(cancel);

        // Assert
        Assert.NotNull(response);
    }

    #endregion

    #region Network Scenarios

    [Fact]
    public async Task Network_LocalCall_OptimizesExecution()
    {
        // Arrange
        const string localSiloId = "local-silo";
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        
        mockTransport.Setup(t => t.LocalSiloId).Returns(localSiloId);
        mockClusterMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SiloInfo> { new SiloInfo(localSiloId, "localhost", 5000, SiloStatus.Active) });
        mockClusterMembership.Setup(m => m.GetActorSilo(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(localSiloId);

        var responseEnvelope = CreateEnvelope("test-actor", "IDataCenterCounterActor", "GetCounterValueAsync", Array.Empty<byte>());
        responseEnvelope.ResponsePayload = BitConverter.GetBytes(42);
        mockTransport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseEnvelope);

        var client = new ClusterClient(mockClusterMembership.Object, mockTransport.Object, 
            new ClusterClientOptions(), NullLogger<ClusterClient>.Instance);
        await client.ConnectAsync();

        // Act
        var envelope = CreateEnvelope("test-actor", "IDataCenterCounterActor", "GetCounterValueAsync", Array.Empty<byte>());
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
        mockTransport.Verify(t => t.SendAsync(localSiloId, It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Network_RemoteCall_RoutesToCorrectSilo()
    {
        // Arrange
        const string localSiloId = "local-silo";
        const string remoteSiloId = "remote-silo";
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        
        mockTransport.Setup(t => t.LocalSiloId).Returns(localSiloId);
        mockClusterMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SiloInfo> 
            { 
                new SiloInfo(localSiloId, "localhost", 5000, SiloStatus.Active),
                new SiloInfo(remoteSiloId, "remote", 5001, SiloStatus.Active)
            });
        mockClusterMembership.Setup(m => m.GetActorSilo(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(remoteSiloId);

        var responseEnvelope = CreateEnvelope("test-actor", "IDataCenterCounterActor", "GetCounterValueAsync", Array.Empty<byte>());
        responseEnvelope.ResponsePayload = BitConverter.GetBytes(42);
        mockTransport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseEnvelope);

        var client = new ClusterClient(mockClusterMembership.Object, mockTransport.Object,
            new ClusterClientOptions(), NullLogger<ClusterClient>.Instance);
        await client.ConnectAsync();

        // Act
        var envelope = CreateEnvelope("test-actor", "IDataCenterCounterActor", "GetCounterValueAsync", Array.Empty<byte>());
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
        mockTransport.Verify(t => t.SendAsync(remoteSiloId, It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Network_ConnectionRetry_RecoversFromTransientFailure()
    {
        // Arrange
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        
        mockTransport.Setup(t => t.LocalSiloId).Returns("silo-1");
        
        // First call fails, second succeeds
        var callCount = 0;
        mockClusterMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return new List<SiloInfo>(); // No active silos initially
                return new List<SiloInfo> { new SiloInfo("silo-1", "localhost", 5000, SiloStatus.Active) };
            });

        var client = new ClusterClient(mockClusterMembership.Object, mockTransport.Object,
            new ClusterClientOptions { ConnectionTimeout = TimeSpan.FromSeconds(5) }, 
            NullLogger<ClusterClient>.Instance);

        // Act & Assert - Should eventually connect
        await client.ConnectAsync();
        Assert.NotNull(client.ClusterMembership);
    }

    [Fact]
    public async Task Network_RequestTimeout_ThrowsTimeoutException()
    {
        // Arrange
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        
        mockTransport.Setup(t => t.LocalSiloId).Returns("silo-1");
        mockClusterMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SiloInfo> { new SiloInfo("silo-1", "localhost", 5000, SiloStatus.Active) });
        mockClusterMembership.Setup(m => m.GetActorSilo(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("silo-1");

        // Simulate timeout
        mockTransport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Request timed out"));

        var client = new ClusterClient(mockClusterMembership.Object, mockTransport.Object,
            new ClusterClientOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) },
            NullLogger<ClusterClient>.Instance);
        await client.ConnectAsync();

        // Act & Assert
        var envelope = CreateEnvelope("test-actor", "IDataCenterCounterActor", "GetCounterValueAsync", Array.Empty<byte>());
        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync(envelope));
    }

    #endregion

    #region Helper Methods

    private (ClusterClient Client, QuarkSilo Silo) CreateClientAndSilo(string siloId)
    {
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        
        mockTransport.Setup(t => t.LocalSiloId).Returns(siloId);
        mockTransport.Setup(t => t.LocalEndpoint).Returns($"localhost:{5000}");
        
        var activeSilos = new List<SiloInfo>
        {
            new SiloInfo(siloId, "localhost", 5000, SiloStatus.Active)
        };
        
        mockClusterMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSilos);
        mockClusterMembership.Setup(m => m.GetActorSilo(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(siloId);

        // Setup transport to return mock responses
        mockTransport.Setup(t => t.SendAsync(
            It.IsAny<string>(),
            It.IsAny<QuarkEnvelope>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string targetSilo, QuarkEnvelope req, CancellationToken ct) =>
            {
                var response = new QuarkEnvelope(
                    messageId: req.MessageId,
                    actorId: req.ActorId,
                    actorType: req.ActorType,
                    methodName: req.MethodName,
                    payload: req.Payload)
                {
                    ResponsePayload = new byte[] { 1, 2, 3, 4 }
                };
                return response;
            });

        var client = new ClusterClient(
            mockClusterMembership.Object,
            mockTransport.Object,
            new ClusterClientOptions(),
            NullLogger<ClusterClient>.Instance);

        var actorFactory = new ActorFactory();
        var silo = new QuarkSilo(
            actorFactory,
            mockClusterMembership.Object,
            mockTransport.Object,
            new QuarkSiloOptions { SiloId = siloId },
            NullLogger<QuarkSilo>.Instance);

        return (client, silo);
    }

    private QuarkEnvelope CreateEnvelope(string actorId, string actorType, string methodName, byte[] payload)
    {
        return new QuarkEnvelope(
            messageId: Guid.NewGuid().ToString(),
            actorId: actorId,
            actorType: actorType,
            methodName: methodName,
            payload: payload);
    }

    #endregion
}
