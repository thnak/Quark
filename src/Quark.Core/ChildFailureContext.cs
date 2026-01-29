namespace Quark.Core;

/// <summary>
/// Context information about a child actor failure.
/// Provides details needed for supervision decision-making.
/// </summary>
public sealed class ChildFailureContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChildFailureContext"/> class.
    /// </summary>
    /// <param name="child">The child actor that failed.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    public ChildFailureContext(IActor child, Exception exception)
    {
        Child = child ?? throw new ArgumentNullException(nameof(child));
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    /// <summary>
    /// Gets the child actor that failed.
    /// </summary>
    public IActor Child { get; }

    /// <summary>
    /// Gets the exception that caused the failure.
    /// </summary>
    public Exception Exception { get; }
}
