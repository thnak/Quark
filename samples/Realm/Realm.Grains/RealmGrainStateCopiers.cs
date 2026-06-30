using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization.Abstractions.Abstractions;

namespace Realm.Grains;

public static class RealmGrainStateCopiers
{
    public static IServiceCollection AddRealmGrainStateCopiers(this IServiceCollection services)
    {
        services.AddSingleton<IDeepCopier<PlayerState>>(
            sp => new PlayerStateCopier(sp.GetRequiredService<ICopierProvider>()));
        return services;
    }
}
