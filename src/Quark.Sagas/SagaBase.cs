using Microsoft.Extensions.Logging;

namespace Quark.Sagas;

/// <summary>
/// Base implementation of a saga that orchestrates multi-step distributed transactions.
/// </summary>
/// <typeparam name="TContext">The type of context passed between saga steps.</typeparam>
public abstract class SagaBase<TContext> : ISaga<TContext>
{
    private readonly ILogger _logger;
    private readonly ISagaStateStore _stateStore;
    private readonly List<ISagaStep<TContext>> _steps = new();

    /// <inheritdoc />
    public string SagaId { get; }

    /// <inheritdoc />
    public IReadOnlyList<ISagaStep<TContext>> Steps => _steps.AsReadOnly();

    /// <inheritdoc />
    public SagaState State { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaBase{TContext}"/> class.
    /// </summary>
    /// <param name="sagaId">The unique identifier for this saga instance.</param>
    /// <param name="stateStore">The state store for persisting saga progress.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    protected SagaBase(string sagaId, ISagaStateStore stateStore, ILogger logger)
    {
        SagaId = sagaId ?? throw new ArgumentNullException(nameof(sagaId));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        State = new SagaState
        {
            SagaId = sagaId,
            Status = SagaStatus.NotStarted,
            StartedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Adds a step to the saga.
    /// </summary>
    /// <param name="step">The saga step to add.</param>
    protected void AddStep(ISagaStep<TContext> step)
    {
        _steps.Add(step ?? throw new ArgumentNullException(nameof(step)));
    }

    /// <inheritdoc />
    public async Task<SagaStatus> ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Load existing state if resuming
            var existingState = await _stateStore.LoadStateAsync(SagaId, cancellationToken);
            if (existingState != null)
            {
                State = existingState;
                _logger.LogInformation("Resuming saga {SagaId} from step {Step}", SagaId, State.CurrentStepIndex);
            }
            else
            {
                State.Status = SagaStatus.Running;
                await _stateStore.SaveStateAsync(State, cancellationToken);
            }

            // Execute steps from current position
            for (int i = State.CurrentStepIndex; i < _steps.Count; i++)
            {
                var step = _steps[i];
                _logger.LogInformation("Executing saga step {StepName} ({Index}/{Total})",
                    step.Name, i + 1, _steps.Count);

                try
                {
                    await step.ExecuteAsync(context, cancellationToken);
                    State.CompletedSteps.Add(step.Name);
                    State.CurrentStepIndex = i + 1;
                    await _stateStore.SaveStateAsync(State, cancellationToken);

                    _logger.LogInformation("Completed saga step {StepName}", step.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Saga step {StepName} failed", step.Name);
                    State.FailureReason = $"Step '{step.Name}' failed: {ex.Message}";
                    await _stateStore.SaveStateAsync(State, cancellationToken);

                    // Compensate all completed steps
                    await CompensateAsync(context, cancellationToken);
                    return State.Status;
                }
            }

            // All steps completed successfully
            State.Status = SagaStatus.Completed;
            State.CompletedAt = DateTimeOffset.UtcNow;
            await _stateStore.SaveStateAsync(State, cancellationToken);

            _logger.LogInformation("Saga {SagaId} completed successfully", SagaId);
            return SagaStatus.Completed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saga {SagaId} execution failed", SagaId);
            State.Status = SagaStatus.CompensationFailed;
            State.FailureReason = ex.Message;
            await _stateStore.SaveStateAsync(State, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CompensateAsync(TContext context, CancellationToken cancellationToken = default)
    {
        State.Status = SagaStatus.Compensating;
        await _stateStore.SaveStateAsync(State, cancellationToken);

        _logger.LogWarning("Starting compensation for saga {SagaId}", SagaId);

        // Compensate steps in reverse order
        for (int i = State.CompletedSteps.Count - 1; i >= 0; i--)
        {
            var stepName = State.CompletedSteps[i];
            var step = _steps.FirstOrDefault(s => s.Name == stepName);

            if (step == null)
            {
                _logger.LogWarning("Cannot find step {StepName} for compensation", stepName);
                continue;
            }

            try
            {
                _logger.LogInformation("Compensating saga step {StepName}", step.Name);
                await step.CompensateAsync(context, cancellationToken);
                State.CompensatedSteps.Add(step.Name);
                await _stateStore.SaveStateAsync(State, cancellationToken);

                _logger.LogInformation("Compensated saga step {StepName}", step.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compensate saga step {StepName}", step.Name);
                State.Status = SagaStatus.CompensationFailed;
                State.FailureReason = $"Compensation of step '{step.Name}' failed: {ex.Message}";
                await _stateStore.SaveStateAsync(State, cancellationToken);
                throw;
            }
        }

        State.Status = SagaStatus.Compensated;
        State.CompletedAt = DateTimeOffset.UtcNow;
        await _stateStore.SaveStateAsync(State, cancellationToken);

        _logger.LogWarning("Saga {SagaId} compensated successfully", SagaId);
    }
}
