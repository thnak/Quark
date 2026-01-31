namespace Quark.Placement.Locality;

/// <summary>
/// Analyzes communication patterns between actors to optimize placement.
/// </summary>
public interface ICommunicationPatternAnalyzer
{
    /// <summary>
    /// Records an interaction between two actors.
    /// </summary>
    /// <param name="fromActorId">The source actor ID.</param>
    /// <param name="toActorId">The destination actor ID.</param>
    /// <param name="messageSize">The size of the message in bytes.</param>
    void RecordInteraction(string fromActorId, string toActorId, long messageSize);

    /// <summary>
    /// Gets the communication graph for the specified time window.
    /// </summary>
    /// <param name="window">The time window to analyze.</param>
    /// <returns>The communication graph.</returns>
    Task<CommunicationGraph> GetCommunicationGraphAsync(TimeSpan window);

    /// <summary>
    /// Gets the top N most frequently communicating actor pairs.
    /// </summary>
    /// <param name="topN">The number of pairs to return.</param>
    /// <returns>A list of actor pairs sorted by communication frequency.</returns>
    Task<IReadOnlyList<ActorPair>> GetHotPairsAsync(int topN);

    /// <summary>
    /// Clears communication history older than the specified age.
    /// </summary>
    /// <param name="maxAge">The maximum age of data to retain.</param>
    void ClearOldData(TimeSpan maxAge);
}
