namespace Quark.Abstractions;

/// <summary>
///     Context information about a child actor failure.
/// </summary>
public sealed class ChildFailureContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ChildFailureContext" /> class.
    /// </summary>
    public ChildFailureContext(IActor child, Exception exception)
    {
        Child = child ?? throw new ArgumentNullException(nameof(child));
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    /// <summary>
    ///     Gets the child actor that failed.
    /// </summary>
    public IActor Child { get; }

    /// <summary>
    ///     Gets the exception that caused the failure.
    /// </summary>
    public Exception Exception { get; }
}