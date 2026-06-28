using Quark.Core.Abstractions.Grains;

namespace Bank.GrainInterfaces;

/// <summary>
///     Buffers statement lines in a lazily, asynchronously initialized resource backed by
///     <c>IManagedActivationMemory&lt;StatementBuffer&gt;</c>. The buffer is created on first use and
///     flushed when the grain deactivates.
/// </summary>
public interface IStatementGrain : IGrainWithStringKey
{
    /// <summary>Appends a line to the buffered statement (initializes the buffer on first call).</summary>
    Task AddLineAsync(string line);

    /// <summary>Returns the buffered statement.</summary>
    Task<string> RenderAsync();

    /// <summary>Returns how many times the buffer was initialized — should stay 1 per activation.</summary>
    Task<int> GetInitCountAsync();

    /// <summary>Deactivates the grain, triggering the managed resource's flush-on-cleanup callback.</summary>
    Task CloseAsync();
}
