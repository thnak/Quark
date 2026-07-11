using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Performance.UserServiceProviderFactory;

/// <summary>
/// Opted-in path: <see cref="ExpensiveUserRepository"/> lives in a Singleton, dedicated provider
/// built once per grain type at silo startup (<see cref="CreateUserServiceProvider"/>) and cached
/// for every subsequent call to any activation of this grain type.
/// </summary>
public sealed class OptedInBehavior : IGrainBehavior, IExpensiveGrain, IGrainUserServiceProviderFactory
{
    private readonly ExpensiveUserRepository _repo;

    public OptedInBehavior(ExpensiveUserRepository repo) => _repo = repo;

    public static IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExpensiveUserRepository>();
        return services.BuildServiceProvider();
    }

    public ValueTask<int> GetConnectionCountAsync() => new(_repo.ConnectionCount);
}
