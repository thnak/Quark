namespace Quark.Runtime;

internal interface IUserServiceProviderRegistry
{
    void Register(GrainType grainType, IServiceProvider provider);

    bool TryGet(GrainType grainType, out IServiceProvider? provider);
}
