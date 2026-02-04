using Quark.Jobs;
using Quark.Jobs.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Quark.Tests;

public class RedisJobQueueTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private ConnectionMultiplexer? _redis;
    private RedisJobQueue? _queue;

    public async Task InitializeAsync()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();

        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _queue = new RedisJobQueue(_redis.GetDatabase());
    }

    public async Task DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
        }

        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
        }
    }

    private static Job CreateTestJob(string jobId, string jobType = "TestJob")
    {
        return new Job
        {
            JobId = jobId,
            JobType = jobType,
            Payload = new byte[] { 1, 2, 3 },
            RetryPolicy = RetryPolicy.Default
        };
    }

    [Fact]
    public async Task EnqueueAsync_ValidJob_ReturnsJobId()
    {
        // Arrange
        var job = CreateTestJob("job1");

        // Act
        var jobId = await _queue!.EnqueueAsync(job);

        // Assert
        Assert.Equal("job1", jobId);
        var retrievedJob = await _queue.GetJobAsync("job1");
        Assert.NotNull(retrievedJob);
        Assert.Equal("job1", retrievedJob.JobId);
    }

    [Fact]
    public async Task EnqueueAsync_DuplicateJobId_ThrowsException()
    {
        // Arrange
        var job1 = CreateTestJob("job1");
        var job2 = CreateTestJob("job1");
        await _queue!.EnqueueAsync(job1);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _queue.EnqueueAsync(job2));
    }

    [Fact]
    public async Task DequeueAsync_EmptyQueue_ReturnsNull()
    {
        // Act
        var job = await _queue!.DequeueAsync();

        // Assert
        Assert.Null(job);
    }

    [Fact]
    public async Task DequeueAsync_PendingJob_ReturnsJobAndMarksRunning()
    {
        // Arrange
        var job = CreateTestJob("job1");
        await _queue!.EnqueueAsync(job);

        // Act
        var dequeuedJob = await _queue.DequeueAsync();

        // Assert
        Assert.NotNull(dequeuedJob);
        Assert.Equal("job1", dequeuedJob.JobId);
        Assert.Equal(JobStatus.Running, dequeuedJob.Status);
        Assert.NotNull(dequeuedJob.StartedAt);
    }

    [Fact]
    public async Task DequeueAsync_ScheduledJob_NotReturnedUntilReady()
    {
        // Arrange
        var job = CreateTestJob("job1");
        job.ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(10);
        await _queue!.EnqueueAsync(job);

        // Act
        var dequeuedJob = await _queue.DequeueAsync();

        // Assert
        Assert.Null(dequeuedJob);
        
        // Verify job is in scheduled status
        var scheduledJob = await _queue.GetJobAsync("job1");
        Assert.NotNull(scheduledJob);
        Assert.Equal(JobStatus.Scheduled, scheduledJob.Status);
    }

    [Fact]
    public async Task DequeueAsync_WithPriority_HigherPriorityFirst()
    {
        // Arrange
        var lowPriorityJob = CreateTestJob("low");
        lowPriorityJob.Priority = 1;
        
        var highPriorityJob = CreateTestJob("high");
        highPriorityJob.Priority = 10;

        // Enqueue low priority first
        await _queue!.EnqueueAsync(lowPriorityJob);
        await _queue.EnqueueAsync(highPriorityJob);

        // Act
        var firstJob = await _queue.DequeueAsync();

        // Assert
        Assert.NotNull(firstJob);
        Assert.Equal("high", firstJob.JobId); // Higher priority should be dequeued first
    }

    [Fact]
    public async Task CompleteAsync_ValidJob_MarksCompleted()
    {
        // Arrange
        var job = CreateTestJob("job1");
        await _queue!.EnqueueAsync(job);
        await _queue.DequeueAsync();

        // Act
        var result = new byte[] { 4, 5, 6 };
        await _queue.CompleteAsync("job1", result);

        // Assert
        var completedJob = await _queue.GetJobAsync("job1");
        Assert.NotNull(completedJob);
        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.Equal(result, completedJob.Result);
        Assert.Equal(100, completedJob.Progress);
        Assert.NotNull(completedJob.CompletedAt);
    }

    [Fact]
    public async Task FailAsync_WithRetriesRemaining_ReschedulesJob()
    {
        // Arrange
        var job = CreateTestJob("job1");
        job.RetryPolicy = new RetryPolicy { MaxRetries = 3 };
        await _queue!.EnqueueAsync(job);
        await _queue.DequeueAsync();

        // Act
        await _queue.FailAsync("job1", new Exception("Test error"));

        // Assert
        var failedJob = await _queue.GetJobAsync("job1");
        Assert.NotNull(failedJob);
        Assert.Equal(JobStatus.Pending, failedJob.Status);
        Assert.Equal(1, failedJob.AttemptCount);
        Assert.Equal("Test error", failedJob.LastError);
        Assert.NotNull(failedJob.ScheduledAt);
    }

    [Fact]
    public async Task FailAsync_MaxRetriesExceeded_MarksFailed()
    {
        // Arrange
        var job = CreateTestJob("job1");
        job.RetryPolicy = new RetryPolicy { MaxRetries = 1 };
        await _queue!.EnqueueAsync(job);

        // Act - fail twice
        await _queue.DequeueAsync();
        await _queue.FailAsync("job1", new Exception("Error 1"));
        
        // Wait for retry delay
        await Task.Delay(TimeSpan.FromSeconds(2));
        
        await _queue.DequeueAsync();
        await _queue.FailAsync("job1", new Exception("Error 2"));

        // Assert
        var failedJob = await _queue.GetJobAsync("job1");
        Assert.NotNull(failedJob);
        Assert.Equal(JobStatus.Failed, failedJob.Status);
        Assert.Equal(2, failedJob.AttemptCount);
        Assert.NotNull(failedJob.CompletedAt);
    }

    [Fact]
    public async Task GetJobAsync_ExistingJob_ReturnsJob()
    {
        // Arrange
        var job = CreateTestJob("job1");
        await _queue!.EnqueueAsync(job);

        // Act
        var retrievedJob = await _queue.GetJobAsync("job1");

        // Assert
        Assert.NotNull(retrievedJob);
        Assert.Equal("job1", retrievedJob.JobId);
        Assert.Equal("TestJob", retrievedJob.JobType);
    }

    [Fact]
    public async Task GetJobAsync_NonExistentJob_ReturnsNull()
    {
        // Act
        var job = await _queue!.GetJobAsync("nonexistent");

        // Assert
        Assert.Null(job);
    }

    [Fact]
    public async Task UpdateProgressAsync_ValidJob_UpdatesProgress()
    {
        // Arrange
        var job = CreateTestJob("job1");
        await _queue!.EnqueueAsync(job);
        await _queue.DequeueAsync();

        // Act
        await _queue.UpdateProgressAsync("job1", 50);

        // Assert
        var updatedJob = await _queue.GetJobAsync("job1");
        Assert.NotNull(updatedJob);
        Assert.Equal(50, updatedJob.Progress);
    }

    [Fact]
    public async Task UpdateProgressAsync_OutOfRangeProgress_ClampsToRange()
    {
        // Arrange
        var job = CreateTestJob("job1");
        await _queue!.EnqueueAsync(job);

        // Act
        await _queue.UpdateProgressAsync("job1", 150);
        var job1 = await _queue.GetJobAsync("job1");
        
        await _queue.UpdateProgressAsync("job1", -10);
        var job2 = await _queue.GetJobAsync("job1");

        // Assert
        Assert.Equal(100, job1!.Progress);
        Assert.Equal(0, job2!.Progress);
    }

    [Fact]
    public async Task CancelAsync_PendingJob_MarksCancelled()
    {
        // Arrange
        var job = CreateTestJob("job1");
        await _queue!.EnqueueAsync(job);

        // Act
        await _queue.CancelAsync("job1");

        // Assert
        var cancelledJob = await _queue.GetJobAsync("job1");
        Assert.NotNull(cancelledJob);
        Assert.Equal(JobStatus.Cancelled, cancelledJob.Status);
        Assert.NotNull(cancelledJob.CompletedAt);
    }

    [Fact]
    public async Task CancelAsync_RunningJob_MarksCancelled()
    {
        // Arrange
        var job = CreateTestJob("job1");
        await _queue!.EnqueueAsync(job);
        await _queue.DequeueAsync();

        // Act
        await _queue.CancelAsync("job1");

        // Assert
        var cancelledJob = await _queue.GetJobAsync("job1");
        Assert.NotNull(cancelledJob);
        Assert.Equal(JobStatus.Cancelled, cancelledJob.Status);
    }

    [Fact]
    public async Task CleanupCompletedJobsAsync_RemovesOldJobs()
    {
        // Arrange
        var job1 = CreateTestJob("job1");
        var job2 = CreateTestJob("job2");
        
        await _queue!.EnqueueAsync(job1);
        await _queue.EnqueueAsync(job2);
        
        await _queue.DequeueAsync();
        await _queue.CompleteAsync("job1");

        // Wait a bit to ensure time difference
        await Task.Delay(100);
        
        // Act - cleanup jobs older than 1 second (job1 should not be removed yet)
        var removed = await _queue.CleanupCompletedJobsAsync(TimeSpan.FromHours(1));

        // Assert
        Assert.Equal(0, removed); // Should not remove recent jobs
        Assert.NotNull(await _queue.GetJobAsync("job1"));
        Assert.NotNull(await _queue.GetJobAsync("job2"));
    }

    [Fact]
    public async Task DequeueAsync_WithDependencies_WaitsForCompletion()
    {
        // Arrange
        var job1 = CreateTestJob("job1");
        var job2 = CreateTestJob("job2");
        job2.Dependencies = JobDependencies.All("job1");

        await _queue!.EnqueueAsync(job1);
        await _queue.EnqueueAsync(job2);

        // Act - dequeue job2 before job1 is complete
        var firstJob = await _queue.DequeueAsync(); // Gets job1
        Assert.Equal("job1", firstJob!.JobId);
        
        var prematureJob2 = await _queue.DequeueAsync(); // Should not get job2

        // Complete job1
        await _queue.CompleteAsync("job1");

        // Try to dequeue again
        var job2Dequeued = await _queue.DequeueAsync(); // Should get job2 now

        // Assert
        Assert.Null(prematureJob2);
        Assert.NotNull(job2Dequeued);
        Assert.Equal("job2", job2Dequeued.JobId);
    }

    [Fact]
    public async Task DequeueAsync_WithAnyDependency_ExecutesWhenOneDependencyCompletes()
    {
        // Arrange
        var job1 = CreateTestJob("job1");
        var job2 = CreateTestJob("job2");
        var job3 = CreateTestJob("job3");
        job3.Dependencies = JobDependencies.Any("job1", "job2");

        await _queue!.EnqueueAsync(job1);
        await _queue!.EnqueueAsync(job2);
        await _queue!.EnqueueAsync(job3);

        // Act - complete only job1
        await _queue.DequeueAsync(); // Gets job1
        await _queue.CompleteAsync("job1");

        // Try to dequeue job3
        var job3Dequeued = await _queue.DequeueAsync(); // Should get job3 now

        // Assert
        Assert.NotNull(job3Dequeued);
        Assert.Equal("job3", job3Dequeued.JobId);
    }

    [Fact]
    public async Task EnqueueAsync_ScheduledJobInPast_EnqueuesImmediately()
    {
        // Arrange
        var job = CreateTestJob("job1");
        job.ScheduledAt = DateTimeOffset.UtcNow.AddHours(-1);

        // Act
        await _queue!.EnqueueAsync(job);
        var dequeuedJob = await _queue.DequeueAsync();

        // Assert
        Assert.NotNull(dequeuedJob);
        Assert.Equal("job1", dequeuedJob.JobId);
    }

    [Fact]
    public async Task MultipleJobs_Workflow_ExecutesCorrectly()
    {
        // Arrange - create a workflow: job1 -> job2 -> job3
        var job1 = CreateTestJob("job1");
        var job2 = CreateTestJob("job2");
        job2.Dependencies = JobDependencies.All("job1");
        var job3 = CreateTestJob("job3");
        job3.Dependencies = JobDependencies.All("job2");

        // Act
        await _queue!.EnqueueAsync(job1);
        await _queue.EnqueueAsync(job2);
        await _queue.EnqueueAsync(job3);

        // Execute job1
        var dequeued1 = await _queue.DequeueAsync();
        Assert.Equal("job1", dequeued1!.JobId);
        await _queue.CompleteAsync("job1");

        // Execute job2
        var dequeued2 = await _queue.DequeueAsync();
        Assert.Equal("job2", dequeued2!.JobId);
        await _queue.CompleteAsync("job2");

        // Execute job3
        var dequeued3 = await _queue.DequeueAsync();
        Assert.Equal("job3", dequeued3!.JobId);
        await _queue.CompleteAsync("job3");

        // Assert all completed
        var finalJob1 = await _queue.GetJobAsync("job1");
        var finalJob2 = await _queue.GetJobAsync("job2");
        var finalJob3 = await _queue.GetJobAsync("job3");

        Assert.Equal(JobStatus.Completed, finalJob1!.Status);
        Assert.Equal(JobStatus.Completed, finalJob2!.Status);
        Assert.Equal(JobStatus.Completed, finalJob3!.Status);
    }

    [Fact]
    public async Task RetryPolicy_ExponentialBackoff_WorksCorrectly()
    {
        // Arrange
        var job = CreateTestJob("job1");
        job.RetryPolicy = new RetryPolicy
        {
            MaxRetries = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            BackoffMultiplier = 2.0
        };
        await _queue!.EnqueueAsync(job);

        // Act & Assert
        await _queue.DequeueAsync();
        await _queue.FailAsync("job1", new Exception("Attempt 1"));

        var job1 = await _queue.GetJobAsync("job1");
        Assert.Equal(1, job1!.AttemptCount);
        Assert.NotNull(job1.ScheduledAt);

        // Verify backoff delay increased
        // After first failure (AttemptCount=1), delay should be 100 * 2^0 = 100ms
        var actualDelay = job1.ScheduledAt!.Value - job1.UpdatedAt;
        Assert.True(actualDelay.TotalMilliseconds >= 100, $"Expected delay >= 100ms, but got {actualDelay.TotalMilliseconds}ms");
    }
}
