using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.UserServiceProviderFactory;

/// <summary>
/// Default path: <see cref="ExpensiveUserRepository"/> is registered Scoped on silo.Services and
/// re-resolved from a fresh <see cref="IServiceScope"/> on every grain call.
/// </summary>
public sealed class NotOptedInBehavior : IGrainBehavior, IExpensiveGrain
{
    private readonly ExpensiveUserRepository _repo;

    public NotOptedInBehavior(ExpensiveUserRepository repo) => _repo = repo;

    public ValueTask<int> GetConnectionCountAsync() => new(_repo.ConnectionCount);
}
