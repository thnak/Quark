using Microsoft.Extensions.Logging;

namespace Quark.Sagas;

/// <summary>
/// Default implementation of a saga coordinator.
/// </summary>
/// <typeparam name="TContext">The type of context passed between saga steps.</typeparam>
public class SagaCoordinator<TContext> : ISagaCoordinator<TContext>
{
    private readonly ISagaStateStore _stateStore;
    private readonly ILogger<SagaCoordinator<TContext>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaCoordinator{TContext}"/> class.
    /// </summary>
    /// <param name="stateStore">The state store for persisting saga state.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public SagaCoordinator(ISagaStateStore stateStore, ILogger<SagaCoordinator<TContext>> logger)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SagaStatus> StartSagaAsync(
        ISaga<TContext> saga,
        TContext context,
        CancellationToken cancellationToken = default)
    {
        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

        _logger.LogInformation("Starting saga {SagaId}", saga.SagaId);

        // Check if saga already exists
        var existingState = await _stateStore.LoadStateAsync(saga.SagaId, cancellationToken);
        if (existingState != null)
        {
            _logger.LogWarning("Saga {SagaId} already exists with status {Status}",
                saga.SagaId, existingState.Status);
            throw new InvalidOperationException(
                $"Saga {saga.SagaId} already exists with status {existingState.Status}");
        }

        return await saga.ExecuteAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SagaStatus> ResumeSagaAsync(
        string sagaId,
        ISaga<TContext> saga,
        TContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sagaId))
            throw new ArgumentException("Saga ID cannot be null or empty", nameof(sagaId));
        if (saga == null)
            throw new ArgumentNullException(nameof(saga));

        _logger.LogInformation("Resuming saga {SagaId}", sagaId);

        var existingState = await _stateStore.LoadStateAsync(sagaId, cancellationToken);
        if (existingState == null)
        {
            _logger.LogError("Cannot resume saga {SagaId} - not found", sagaId);
            throw new InvalidOperationException($"Saga {sagaId} not found");
        }

        if (existingState.Status == SagaStatus.Completed ||
            existingState.Status == SagaStatus.Compensated)
        {
            _logger.LogWarning("Cannot resume saga {SagaId} - already in terminal state {Status}",
                sagaId, existingState.Status);
            return existingState.Status;
        }

        return await saga.ExecuteAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SagaState?> GetSagaStateAsync(string sagaId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sagaId))
            throw new ArgumentException("Saga ID cannot be null or empty", nameof(sagaId));

        return await _stateStore.LoadStateAsync(sagaId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> RecoverInProgressSagasAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting saga recovery process");

        var runningSagas = await _stateStore.GetSagasByStatusAsync(SagaStatus.Running, cancellationToken);
        var compensatingSagas = await _stateStore.GetSagasByStatusAsync(SagaStatus.Compensating, cancellationToken);

        var allSagas = runningSagas.Concat(compensatingSagas).ToList();

        _logger.LogInformation("Found {Count} sagas to recover", allSagas.Count);

        // Note: Actual recovery requires the saga definition and context,
        // which must be provided by the application. This method just identifies
        // sagas that need recovery.
        return allSagas.Count;
    }
}
