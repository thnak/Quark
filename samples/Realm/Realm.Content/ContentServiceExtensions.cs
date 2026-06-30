using Microsoft.Extensions.DependencyInjection;

namespace Realm.Content;

public static class ContentServiceExtensions
{
    public static IServiceCollection AddRealmContent(this IServiceCollection services)
    {
        services.AddSingleton<RealmContentLoader>();
        return services;
    }
}
