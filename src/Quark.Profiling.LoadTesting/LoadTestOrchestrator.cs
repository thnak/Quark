using System.Collections.Concurrent;
using System.Diagnostics;
using Quark.Abstractions;
using Quark.Profiling.Abstractions;

namespace Quark.Profiling.LoadTesting;

/// <summary>
/// Default implementation of load test orchestrator.
/// Provides built-in load testing capabilities for Quark actors.
/// </summary>
public sealed class LoadTestOrchestrator : ILoadTestOrchestrator
{
    private readonly ConcurrentDictionary<string, LoadTestExecution> _runningTests = new();
    private readonly IActorFactory? _actorFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoadTestOrchestrator"/> class.
    /// </summary>
    /// <param name="actorFactory">Optional actor factory for creating test actors.</param>
    public LoadTestOrchestrator(IActorFactory? actorFactory = null)
    {
        _actorFactory = actorFactory;
    }

    /// <inheritdoc/>
    public async Task<LoadTestResult> StartLoadTestAsync(
        LoadTestScenario scenario,
        CancellationToken cancellationToken = default)
    {
        var execution = new LoadTestExecution
        {
            Scenario = scenario,
            Status = new LoadTestStatus
            {
                TestId = scenario.TestId,
                State = LoadTestState.Initializing
            },
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        };

        _runningTests[scenario.TestId] = execution;

        try
        {
            execution.Status.State = LoadTestState.Running;
            var result = await ExecuteLoadTestAsync(execution);
            execution.Status.State = LoadTestState.Completed;
            execution.Result = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            execution.Status.State = LoadTestState.Cancelled;
            throw;
        }
        catch (Exception)
        {
            execution.Status.State = LoadTestState.Failed;
            throw;
        }
        finally
        {
            _runningTests.TryRemove(scenario.TestId, out _);
        }
    }

    /// <inheritdoc/>
    public LoadTestStatus? GetTestStatus(string testId)
    {
        return _runningTests.TryGetValue(testId, out var execution) ? execution.Status : null;
    }

    /// <inheritdoc/>
    public Task StopLoadTestAsync(string testId, CancellationToken cancellationToken = default)
    {
        if (_runningTests.TryGetValue(testId, out var execution))
        {
            execution.CancellationTokenSource.Cancel();
        }
        return Task.CompletedTask;
    }

    private async Task<LoadTestResult> ExecuteLoadTestAsync(LoadTestExecution execution)
    {
        var scenario = execution.Scenario;
        var sw = Stopwatch.StartNew();
        var startTime = DateTimeOffset.UtcNow;

        var latencies = new ConcurrentBag<double>();
        long successCount = 0;
        long failureCount = 0;
        long totalMessages = 0;

        var tasks = new List<Task>();
        var semaphore = scenario.MessageRateLimit > 0
            ? new SemaphoreSlim(scenario.MessageRateLimit)
            : null;

        for (int i = 0; i < scenario.ConcurrentActors; i++)
        {
            var actorIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                for (int msgIndex = 0; msgIndex < scenario.MessagesPerActor; msgIndex++)
                {
                    if (execution.CancellationTokenSource.Token.IsCancellationRequested)
                        break;

                    if (semaphore != null)
                        await semaphore.WaitAsync(execution.CancellationTokenSource.Token);

                    try
                    {
                        var msgSw = Stopwatch.StartNew();
                        
                        // Simulate message processing
                        // In a real implementation, this would invoke actual actor methods
                        await Task.Delay(1, execution.CancellationTokenSource.Token);
                        
                        msgSw.Stop();
                        latencies.Add(msgSw.Elapsed.TotalMilliseconds);
                        Interlocked.Increment(ref successCount);
                    }
                    catch
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                    finally
                    {
                        Interlocked.Increment(ref totalMessages);
                        semaphore?.Release();
                    }

                    // Update status periodically
                    if (msgIndex % 100 == 0)
                    {
                        var progress = (double)totalMessages / (scenario.ConcurrentActors * scenario.MessagesPerActor) * 100.0;
                        execution.Status.ProgressPercent = progress;
                        execution.Status.MessagesProcessed = totalMessages;
                        execution.Status.CurrentMessagesPerSecond = totalMessages / sw.Elapsed.TotalSeconds;
                    }
                }
            }, execution.CancellationTokenSource.Token));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        var endTime = DateTimeOffset.UtcNow;
        var latencyArray = latencies.OrderBy(x => x).ToArray();

        return new LoadTestResult
        {
            TestId = scenario.TestId,
            StartTime = startTime,
            EndTime = endTime,
            TotalMessages = totalMessages,
            SuccessfulMessages = successCount,
            FailedMessages = failureCount,
            MessagesPerSecond = totalMessages / sw.Elapsed.TotalSeconds,
            Latency = CalculateLatencyStatistics(latencyArray)
        };
    }

    private static LatencyStatistics CalculateLatencyStatistics(double[] sortedLatencies)
    {
        if (sortedLatencies.Length == 0)
        {
            return new LatencyStatistics();
        }

        var min = sortedLatencies[0];
        var max = sortedLatencies[^1];
        var mean = sortedLatencies.Average();
        var p50 = GetPercentile(sortedLatencies, 0.50);
        var p95 = GetPercentile(sortedLatencies, 0.95);
        var p99 = GetPercentile(sortedLatencies, 0.99);
        var p999 = GetPercentile(sortedLatencies, 0.999);
        
        var variance = sortedLatencies.Select(x => Math.Pow(x - mean, 2)).Average();
        var stdDev = Math.Sqrt(variance);

        return new LatencyStatistics
        {
            MinMs = min,
            MaxMs = max,
            MeanMs = mean,
            P50Ms = p50,
            P95Ms = p95,
            P99Ms = p99,
            P999Ms = p999,
            StdDevMs = stdDev
        };
    }

    private static double GetPercentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;
        
        var index = (int)Math.Ceiling(sortedValues.Length * percentile) - 1;
        index = Math.Max(0, Math.Min(sortedValues.Length - 1, index));
        return sortedValues[index];
    }

    private sealed class LoadTestExecution
    {
        public required LoadTestScenario Scenario { get; init; }
        public required LoadTestStatus Status { get; init; }
        public required CancellationTokenSource CancellationTokenSource { get; init; }
        public LoadTestResult? Result { get; set; }
    }
}
