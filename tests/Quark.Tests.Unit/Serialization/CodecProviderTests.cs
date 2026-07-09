using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Serialization.Providers;
using Xunit;

namespace Quark.Tests.Unit.Serialization;

/// <summary>
///     Covers <see cref="CodecProvider.TryGetGeneralizedCodec" />'s lazy-init path (issue #150).
/// </summary>
public sealed class CodecProviderTests
{
    private sealed class FakeGeneralizedCodec : IGeneralizedCodec
    {
        public bool IsSupportedType(Type type) => type == typeof(string);

        public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, object? value)
            => throw new NotSupportedException();

        public object? ReadValue(CodecReader reader, Field field) => throw new NotSupportedException();
    }

    [Fact]
    public void TryGetGeneralizedCodec_ConcurrentFirstAccess_AllCallersSeeSameCodec()
    {
        ServiceCollection services = new();
        services.AddSingleton<IGeneralizedCodec, FakeGeneralizedCodec>();
        ServiceProvider sp = services.BuildServiceProvider();
        CodecProvider provider = new(sp);

        IGeneralizedCodec?[] results = new IGeneralizedCodec?[Environment.ProcessorCount * 4];
        Parallel.For(0, results.Length, i =>
        {
            results[i] = provider.TryGetGeneralizedCodec(typeof(string));
        });

        Assert.All(results, r => Assert.IsType<FakeGeneralizedCodec>(r));
    }

    [Fact]
    public void TryGetGeneralizedCodec_UnsupportedType_ReturnsNull()
    {
        ServiceCollection services = new();
        services.AddSingleton<IGeneralizedCodec, FakeGeneralizedCodec>();
        ServiceProvider sp = services.BuildServiceProvider();
        CodecProvider provider = new(sp);

        Assert.Null(provider.TryGetGeneralizedCodec(typeof(int)));
    }
}
