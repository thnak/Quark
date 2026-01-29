namespace Quark.Abstractions;

/// <summary>
///     Context for tracking actor call chains to prevent reentrancy deadlocks.
///     Propagates through the call stack to detect circular dependencies.
/// </summary>
public sealed class CallChainContext
{
    private static readonly AsyncLocal<CallChainContext?> _current = new();
    private readonly HashSet<string> _callChain;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CallChainContext" /> class.
    /// </summary>
    /// <param name="chainId">The unique chain identifier.</param>
    private CallChainContext(string chainId)
    {
        ChainId = chainId;
        _callChain = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Initializes a new instance with existing call chain.
    /// </summary>
    private CallChainContext(string chainId, HashSet<string> callChain)
    {
        ChainId = chainId;
        _callChain = new HashSet<string>(callChain, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Gets the unique identifier for this call chain.
    /// </summary>
    public string ChainId { get; }

    /// <summary>
    ///     Gets the current call chain context for the executing async flow.
    /// </summary>
    public static CallChainContext? Current => _current.Value;

    /// <summary>
    ///     Creates a new call chain context.
    /// </summary>
    /// <returns>A new call chain context.</returns>
    public static CallChainContext Create()
    {
        return new CallChainContext(Guid.NewGuid().ToString());
    }

    /// <summary>
    ///     Enters an actor in the call chain.
    ///     Throws if the actor is already in the chain (circular dependency detected).
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <returns>A disposable scope that removes the actor when disposed.</returns>
    /// <exception cref="ReentrancyException">Thrown when circular dependency is detected.</exception>
    public IDisposable EnterActor(string actorId, string actorType)
    {
        var key = $"{actorType}:{actorId}";

        if (_callChain.Contains(key))
        {
            var chain = string.Join(" → ", _callChain);
            throw new ReentrancyException(
                $"Circular dependency detected: {chain} → {key}. " +
                $"Actor {actorId} is already in the call chain.");
        }

        _callChain.Add(key);
        return new ActorScope(this, key);
    }

    /// <summary>
    ///     Checks if an actor is in the current call chain.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <returns>True if the actor is in the call chain.</returns>
    public bool IsInCallChain(string actorId, string actorType)
    {
        var key = $"{actorType}:{actorId}";
        return _callChain.Contains(key);
    }

    /// <summary>
    ///     Gets the current call chain as a string.
    /// </summary>
    public string GetCallChainString()
    {
        return string.Join(" → ", _callChain);
    }

    /// <summary>
    ///     Creates a scope that sets this context as current.
    /// </summary>
    /// <returns>A disposable scope.</returns>
    public IDisposable CreateScope()
    {
        var previous = _current.Value;
        _current.Value = this;
        return new ContextScope(previous);
    }

    /// <summary>
    ///     Creates a child context with the same chain ID but separate call chain.
    ///     Used for parallel calls from the same actor.
    /// </summary>
    /// <returns>A child context.</returns>
    public CallChainContext CreateChild()
    {
        return new CallChainContext(ChainId, _callChain);
    }

    private sealed class ActorScope : IDisposable
    {
        private readonly CallChainContext _context;
        private readonly string _key;

        public ActorScope(CallChainContext context, string key)
        {
            _context = context;
            _key = key;
        }

        public void Dispose()
        {
            _context._callChain.Remove(_key);
        }
    }

    private sealed class ContextScope : IDisposable
    {
        private readonly CallChainContext? _previous;

        public ContextScope(CallChainContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            _current.Value = _previous;
        }
    }
}

/// <summary>
///     Exception thrown when a circular dependency (reentrancy) is detected in the call chain.
/// </summary>
public sealed class ReentrancyException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ReentrancyException" /> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public ReentrancyException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReentrancyException" /> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ReentrancyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}