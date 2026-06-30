using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization.Abstractions.Abstractions;
using Realm.Common.Dtos;

namespace Realm.Common;

public static class RealmCommonCopiers
{
    public static IServiceCollection AddRealmCommonCopiers(this IServiceCollection services)
    {
        services.AddSingleton<IDeepCopier<Coord>>(
            sp => new CoordCopier(sp.GetRequiredService<ICopierProvider>()));
        return services;
    }
}
