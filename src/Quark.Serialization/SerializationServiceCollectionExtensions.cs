using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Serialization.Abstractions;
using Quark.Serialization.Codecs;
using Quark.Serialization.Copiers;
using Quark.Serialization.Providers;

namespace Quark.Serialization;

/// <summary>
/// Extension methods for registering Quark serialization into an <see cref="IServiceCollection"/>.
/// </summary>
public static class SerializationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Quark serialization infrastructure with all built-in primitive codecs.
    /// Call this before registering user-defined codecs so the primitives are always available.
    /// </summary>
    public static IServiceCollection AddQuarkSerialization(this IServiceCollection services)
    {
        // Providers
        services.TryAddSingleton<ICodecProvider, CodecProvider>();
        services.TryAddSingleton<ICopierProvider, CopierProvider>();
        services.TryAddSingleton<QuarkSerializer>();
        services.TryAddSingleton<ISerializer>(sp => sp.GetRequiredService<QuarkSerializer>());

        // Primitive codecs
        services.AddPrimitiveCodecs();

        return services;
    }

    /// <summary>
    /// Registers a custom <see cref="IFieldCodec{T}"/> implementation.
    /// Use this for hand-written codecs or for types that cannot use the source generator.
    /// </summary>
    public static IServiceCollection AddCodec<T, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCodec>(this IServiceCollection services)
        where TCodec : class, IFieldCodec<T>
    {
        services.AddSingleton<IFieldCodec<T>, TCodec>();
        return services;
    }

    /// <summary>
    /// Registers a custom <see cref="IDeepCopier{T}"/> implementation.
    /// </summary>
    public static IServiceCollection AddCopier<T, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCopier>(this IServiceCollection services)
        where TCopier : class, IDeepCopier<T>
    {
        services.AddSingleton<IDeepCopier<T>, TCopier>();
        return services;
    }

    /// <summary>Registers a generalized codec that handles multiple types.</summary>
    public static IServiceCollection AddGeneralizedCodec<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCodec>(this IServiceCollection services)
        where TCodec : class, IGeneralizedCodec
    {
        services.AddSingleton<IGeneralizedCodec, TCodec>();
        return services;
    }

    /// <summary>Registers a generalized copier that handles multiple types.</summary>
    public static IServiceCollection AddGeneralizedCopier<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCopier>(this IServiceCollection services)
        where TCopier : class, IGeneralizedCopier
    {
        services.AddSingleton<IGeneralizedCopier, TCopier>();
        return services;
    }

    private static IServiceCollection AddPrimitiveCodecs(this IServiceCollection services)
    {
        // Boolean
        services.TryAddSingleton<IFieldCodec<bool>, BoolCodec>();
        services.TryAddSingleton<IDeepCopier<bool>>(new ImmutableCopier<bool>());

        // Integer types
        services.TryAddSingleton<IFieldCodec<byte>, ByteCodec>();
        services.TryAddSingleton<IDeepCopier<byte>>(new ImmutableCopier<byte>());

        services.TryAddSingleton<IFieldCodec<sbyte>, SByteCodec>();
        services.TryAddSingleton<IDeepCopier<sbyte>>(new ImmutableCopier<sbyte>());

        services.TryAddSingleton<IFieldCodec<short>, Int16Codec>();
        services.TryAddSingleton<IDeepCopier<short>>(new ImmutableCopier<short>());

        services.TryAddSingleton<IFieldCodec<ushort>, UInt16Codec>();
        services.TryAddSingleton<IDeepCopier<ushort>>(new ImmutableCopier<ushort>());

        services.TryAddSingleton<IFieldCodec<int>, Int32Codec>();
        services.TryAddSingleton<IDeepCopier<int>>(new ImmutableCopier<int>());

        services.TryAddSingleton<IFieldCodec<uint>, UInt32Codec>();
        services.TryAddSingleton<IDeepCopier<uint>>(new ImmutableCopier<uint>());

        services.TryAddSingleton<IFieldCodec<long>, Int64Codec>();
        services.TryAddSingleton<IDeepCopier<long>>(new ImmutableCopier<long>());

        services.TryAddSingleton<IFieldCodec<ulong>, UInt64Codec>();
        services.TryAddSingleton<IDeepCopier<ulong>>(new ImmutableCopier<ulong>());

        // Floating point
        services.TryAddSingleton<IFieldCodec<float>, FloatCodec>();
        services.TryAddSingleton<IDeepCopier<float>>(new ImmutableCopier<float>());

        services.TryAddSingleton<IFieldCodec<double>, DoubleCodec>();
        services.TryAddSingleton<IDeepCopier<double>>(new ImmutableCopier<double>());

        services.TryAddSingleton<IFieldCodec<decimal>, DecimalCodec>();
        services.TryAddSingleton<IDeepCopier<decimal>>(new ImmutableCopier<decimal>());

        // Text
        services.TryAddSingleton<IFieldCodec<string?>, StringCodec>();
        services.TryAddSingleton<IDeepCopier<string?>>(new ImmutableCopier<string?>());

        services.TryAddSingleton<IFieldCodec<char>, CharCodec>();
        services.TryAddSingleton<IDeepCopier<char>>(new ImmutableCopier<char>());

        // Other common value types
        services.TryAddSingleton<IFieldCodec<Guid>, GuidCodec>();
        services.TryAddSingleton<IDeepCopier<Guid>>(new ImmutableCopier<Guid>());

        services.TryAddSingleton<IFieldCodec<DateTime>, DateTimeCodec>();
        services.TryAddSingleton<IDeepCopier<DateTime>>(new ImmutableCopier<DateTime>());

        services.TryAddSingleton<IFieldCodec<DateTimeOffset>, DateTimeOffsetCodec>();
        services.TryAddSingleton<IDeepCopier<DateTimeOffset>>(new ImmutableCopier<DateTimeOffset>());

        services.TryAddSingleton<IFieldCodec<TimeSpan>, TimeSpanCodec>();
        services.TryAddSingleton<IDeepCopier<TimeSpan>>(new ImmutableCopier<TimeSpan>());

        services.TryAddSingleton<IFieldCodec<byte[]?>, ByteArrayCodec>();

        return services;
    }
}
