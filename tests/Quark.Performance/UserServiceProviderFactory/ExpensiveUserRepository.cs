namespace Quark.Performance.UserServiceProviderFactory;

/// <summary>
/// Stands in for the class of dependency IGrainUserServiceProviderFactory targets — a repository
/// backed by a connection pool, a rules engine, anything with real, non-trivial construction cost
/// that's effectively stateless/reusable across calls. Construction cost is deterministic (no
/// wall-clock sleeps) so the benchmark stays reproducible across machines.
/// </summary>
public sealed class ExpensiveUserRepository
{
    private readonly Dictionary<int, string> _connectionPool;

    public ExpensiveUserRepository()
    {
        _connectionPool = new Dictionary<int, string>(64);
        for (int i = 0; i < 64; i++)
        {
            _connectionPool[i] = Guid.NewGuid().ToString();
        }
    }

    public int ConnectionCount => _connectionPool.Count;
}
