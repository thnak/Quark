using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.CodeGenerator;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class GrainProxyGeneratorTests
{
    [Fact]
    public void Generates_Proxy_For_Grain_Interface()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task<long> IncrementAsync();
                                  ValueTask ResetAsync();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains("internal sealed class CounterGrainProxy", generated);
        Assert.Contains(": global::Demo.ICounterGrain", generated);
        Assert.Contains(
            ", global::Quark.Core.Abstractions.Hosting.IGrainProxyActivator<CounterGrainProxy>",
            generated);
        Assert.Contains("public static CounterGrainProxy Create(", generated);
        Assert.Contains("=> new CounterGrainProxy(grainId, invoker);", generated);
        Assert.Contains("internal readonly struct CounterGrainProxy_IncrementAsyncInvokable", generated);
        Assert.Contains("internal readonly struct CounterGrainProxy_ResetAsyncInvokable", generated);
        // Invokable structs must have a Clone() method for data isolation.
        Assert.Contains("public CounterGrainProxy_IncrementAsyncInvokable Clone()", generated);
        Assert.Contains("public CounterGrainProxy_ResetAsyncInvokable Clone()", generated);
        // No-arg invokables: Clone() returns this (zero cost).
        Assert.Contains("return this;", generated);
        // Invokable structs must have Serialize / static Deserialize for the transport path.
        Assert.Contains("public void Serialize(ref global::Quark.Serialization.Abstractions.Buffers.CodecWriter writer)", generated);
        Assert.Contains("public static CounterGrainProxy_IncrementAsyncInvokable Deserialize(", generated);
        Assert.Contains("public static CounterGrainProxy_ResetAsyncInvokable Deserialize(", generated);
        // No-arg Serialize is empty body; Deserialize returns new().
        Assert.Contains("{ }", generated);
        Assert.Contains("=> new();", generated);
        // Proxy methods call .Clone() on the new invokable.
        Assert.Contains("_invoker.InvokeAsync<CounterGrainProxy_IncrementAsyncInvokable, long>(_grainId, new CounterGrainProxy_IncrementAsyncInvokable().Clone())", generated);
        Assert.Contains(
            "return new global::System.Threading.Tasks.ValueTask(_invoker.InvokeVoidAsync(_grainId, new CounterGrainProxy_ResetAsyncInvokable().Clone()));",
            generated);
        Assert.Contains("public sealed class CounterGrainProxy_TransportDispatcher", generated);
        Assert.Contains(": global::Quark.Core.Abstractions.Hosting.ITransportGrainDispatcher", generated);
        Assert.Contains("DispatchAsync(", generated);
        // Transport dispatcher uses Deserialize instead of boxed ReadArg.
        Assert.Contains("invoker.InvokeAsync<CounterGrainProxy_IncrementAsyncInvokable, long>(", generated);
        Assert.Contains("invoker.InvokeVoidAsync<CounterGrainProxy_ResetAsyncInvokable>(", generated);
        Assert.DoesNotContain("ReadArg(", generated);
        // Grain proxy implements IGrainProxy and exposes GrainId.
        Assert.Contains(", global::Quark.Core.Abstractions.Hosting.IGrainProxy", generated);
        Assert.Contains("public global::Quark.Core.Abstractions.Identity.GrainId GrainId => _grainId;", generated);
    }

    [Fact]
    public void Generates_Proxy_For_Observer_Interface()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface IMyObserver : IGrainObserver
                              {
                                  Task OnEventAsync(string message);
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains("internal sealed class MyObserverProxy", generated);
        Assert.Contains(": global::Demo.IMyObserver", generated);
        Assert.Contains(
            ", global::Quark.Core.Abstractions.Hosting.IGrainObserverProxyActivator<MyObserverProxy>",
            generated);
        Assert.Contains("public static MyObserverProxy Create(", generated);
        Assert.Contains("=> new MyObserverProxy(grainId, invoker);", generated);
        Assert.Contains("internal readonly struct MyObserverProxy_OnEventAsyncInvokable", generated);
        Assert.Contains(": global::Quark.Core.Abstractions.Hosting.IObserverVoidInvokable", generated);
        Assert.Contains("InvokeObserverAsync(_grainId, new MyObserverProxy_OnEventAsyncInvokable(", generated);
        Assert.Contains("public sealed class MyObserverProxy_TransportDispatcher", generated);
        Assert.Contains("invoker.InvokeObserverAsync<MyObserverProxy_OnEventAsyncInvokable>(", generated);
        // Observer proxy must NOT implement IGrainProxy.
        Assert.DoesNotContain("IGrainProxy", generated);
    }

    [Fact]
    public void Ignores_Non_Grain_Interfaces()
    {
        const string source = """
                              namespace Demo;

                              public interface IUtilityContract
                              {
                                  void Ping();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generates_GrainRef_Serialize_For_StringKey_Parameter()
    {
        // A grain method that takes an IGrainWithStringKey parameter should produce
        // writer.WriteString(((IGrainProxy)...).GrainId.Key) in Serialize, and
        // factory!.GetGrain<T>(reader.ReadString()) in Deserialize.
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface ITargetGrain : IGrainWithStringKey
                              {
                                  Task PingAsync();
                              }

                              public interface ICallerGrain : IGrainWithStringKey
                              {
                                  Task ForwardAsync(ITargetGrain target);
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        // Two proxies are generated (one per grain interface).
        Assert.Equal(2, result.GeneratedSources.Length);

        // Find the CallerGrain proxy source (the one with the ForwardAsync method).
        string callerSource = result.GeneratedSources.Single(s => s.Contains("CallerGrainProxy"));

        // Serialize must cast to IGrainProxy to read the GrainId key.
        Assert.Contains(
            "writer.WriteString(((global::Quark.Core.Abstractions.Hosting.IGrainProxy)_target).GrainId.Key);",
            callerSource);

        // Deserialize must reconstruct the grain ref via factory.GetGrain<T>(string).
        Assert.Contains(
            "factory!.GetGrain<global::Demo.ITargetGrain>(reader.ReadString())",
            callerSource);
    }

    [Fact]
    public void Generates_Correct_ReadWrite_For_Enum_Parameter_And_Return()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public enum Priority { Low = 0, Normal = 1, High = 2 }

                              public interface ITaskGrain : IGrainWithStringKey
                              {
                                  Task<Priority> GetPriorityAsync();
                                  Task SetPriorityAsync(Priority priority);
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Enum parameter write: cast to underlying int and write
        Assert.Contains("writer.WriteInt32((int)_priority);", generated);
        // Enum parameter deserialize: read int and cast to enum type
        Assert.Contains("global::Demo.Priority _priority = (global::Demo.Priority)reader.ReadInt32();", generated);
        // Return value write in transport dispatcher
        Assert.Contains("writer.WriteInt32((int)_ret);", generated);
        // DeserializeResult: read int and cast to enum type
        Assert.Contains("global::Demo.Priority _ret = (global::Demo.Priority)reader.ReadInt32();", generated);
        // Must not fall back to boxed WriteValue/ReadArg for enum types
        Assert.DoesNotContain("WriteValue(writer, _priority)", generated);
        Assert.DoesNotContain("ReadArg(ref reader)", generated);
    }

    [Fact]
    public void Generates_Correct_ReadWrite_For_Byte_Backed_Enum()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public enum ByteStatus : byte { Off = 0, On = 1 }

                              public interface IStatusGrain : IGrainWithStringKey
                              {
                                  Task SetStatusAsync(ByteStatus status);
                                  Task<ByteStatus> GetStatusAsync();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Byte-backed enum: write as byte, read as byte with cast
        Assert.Contains("writer.WriteByte((byte)_status);", generated);
        Assert.Contains("global::Demo.ByteStatus _status = (global::Demo.ByteStatus)reader.ReadByte();", generated);
        Assert.Contains("writer.WriteByte((byte)_ret);", generated);
        Assert.Contains("global::Demo.ByteStatus _ret = (global::Demo.ByteStatus)reader.ReadByte();", generated);
    }

    [Fact]
    public void Preserves_Nullable_Reference_Type_In_Return_Type()
    {
        // Regression test for https://github.com/thnak/Quark/issues/83
        // The proxy generator was dropping '?' from nullable reference type return values,
        // causing the generated proxy method signature to not match the interface.
        const string source = """
                              #nullable enable
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public class MonsterInfo { }

                              public interface IMonsterGrain : IGrainWithStringKey
                              {
                                  Task<MonsterInfo?> GetInfoAsync();
                                  Task<string?> GetNameAsync();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // The proxy method and invokable struct must preserve the nullable '?' annotation.
        // MonsterInfo? → fully qualified user type with '?'
        Assert.Contains("global::Demo.MonsterInfo?>", generated);
        // string? → built-in keyword alias is preserved (UseSpecialTypes means 'string' not 'System.String')
        Assert.Contains("global::System.Threading.Tasks.Task<string?>", generated);
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }
}
