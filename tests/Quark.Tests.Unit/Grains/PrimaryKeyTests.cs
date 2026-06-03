using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class PrimaryKeyTests
{
    private static readonly IGrainFactory NullFactory = new NullGrainFactory();
    private static readonly IServiceProvider NullServices = new NullServiceProvider();

    private static async Task<T> ActivateAsync<T>(T grain, GrainId id)
        where T : Grain
    {
        var ctx = new GrainContext(id, NullFactory, NullServices);
        await ctx.ActivateAsync(grain);
        return grain;
    }

    [Fact]
    public async Task GetPrimaryKeyString_ReturnsKey()
    {
        var grain = await ActivateAsync(new StringKeyTestGrain(),
            new GrainId(new GrainType("G"), "hello"));
        Assert.Equal("hello", grain.ReadKey());
    }

    [Fact]
    public async Task GetPrimaryKey_Guid_ReturnsKey()
    {
        Guid id = Guid.NewGuid();
        var grain = await ActivateAsync(new GuidKeyTestGrain(),
            new GrainId(new GrainType("G"), id.ToString("N")));
        Assert.Equal(id, grain.ReadKey());
    }

    [Fact]
    public async Task GetPrimaryKeyLong_ReturnsKey()
    {
        var grain = await ActivateAsync(new LongKeyTestGrain(),
            new GrainId(new GrainType("G"), "42"));
        Assert.Equal(42L, grain.ReadKey());
    }

    [Fact]
    public async Task GetPrimaryKey_GuidCompound_ReturnsKeyAndExtension()
    {
        Guid id = Guid.NewGuid();
        var grain = await ActivateAsync(new GuidCompoundTestGrain(),
            new GrainId(new GrainType("G"), $"{id:N}+ext"));
        grain.ReadKey(out string ext);
        Assert.Equal(id, grain.ReadKey(out _));
        Assert.Equal("ext", ext);
    }

    [Fact]
    public async Task GetPrimaryKeyLong_Compound_ReturnsKeyAndExtension()
    {
        var grain = await ActivateAsync(new LongCompoundTestGrain(),
            new GrainId(new GrainType("G"), "99+region"));
        Assert.Equal(99L, grain.ReadKey(out string ext));
        Assert.Equal("region", ext);
    }

    // --- test grain helpers ---

    private sealed class StringKeyTestGrain : Grain
    {
        public string ReadKey() => GetPrimaryKeyString();
    }

    private sealed class GuidKeyTestGrain : Grain
    {
        public Guid ReadKey() => GetPrimaryKey();
    }

    private sealed class LongKeyTestGrain : Grain
    {
        public long ReadKey() => GetPrimaryKeyLong();
    }

    private sealed class GuidCompoundTestGrain : Grain
    {
        public Guid ReadKey(out string ext) => GetPrimaryKey(out ext);
    }

    private sealed class LongCompoundTestGrain : Grain
    {
        public long ReadKey(out string ext) => GetPrimaryKeyLong(out ext);
    }

    // --- stubs ---

    private sealed class NullGrainFactory : IGrainFactory
    {
        public TGI GetGrain<TGI>(string key) where TGI : IGrainWithStringKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(long key) where TGI : IGrainWithIntegerKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(Guid key) where TGI : IGrainWithGuidKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(long key, string? ext) where TGI : IGrainWithIntegerCompoundKey => throw new NotImplementedException();
        public TGI GetGrain<TGI>(Guid key, string? ext) where TGI : IGrainWithGuidCompoundKey => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, string key) => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, Guid key) => throw new NotImplementedException();
        public IGrain GetGrain(Type grainInterfaceType, long key) => throw new NotImplementedException();
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
