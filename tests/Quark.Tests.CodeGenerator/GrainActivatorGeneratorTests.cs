using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.CodeGenerator;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class GrainActivatorGeneratorTests
{
    [Fact]
    public void Generates_ActivatorFactory_With_PersistentState_Parameters()
    {
        const string source = """
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Persistence.Abstractions;

                              namespace Demo;

                              public sealed class ProfileGrain : Grain
                              {
                                  public ProfileGrain(
                                      [PersistentState("profile")] IPersistentState<ProfileState> profile,
                                      [PersistentState("settings", "secondary")] IPersistentState<SettingsState> settings)
                                  { }
                              }

                              public sealed class ProfileState { }
                              public sealed class SettingsState { }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainActivatorGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);

        // Factory method now receives GrainId
        Assert.Contains("Create(global::Quark.Core.Abstractions.Identity.GrainId grainId", generated);

        // Default provider uses GetRequiredService<IGrainStorage>
        Assert.Contains(
            "new global::Quark.Persistence.Abstractions.PersistentState<global::Demo.ProfileState>(grainId, \"profile\",",
            generated);
        Assert.Contains("GetRequiredService<global::Quark.Persistence.Abstractions.IGrainStorage>(services)", generated);

        // Named provider uses GetRequiredKeyedService<IGrainStorage>
        Assert.Contains(
            "new global::Quark.Persistence.Abstractions.PersistentState<global::Demo.SettingsState>(grainId, \"settings\",",
            generated);
        Assert.Contains("GetRequiredKeyedService<global::Quark.Persistence.Abstractions.IGrainStorage>(services, \"secondary\")", generated);
    }

    [Fact]
    public void Generates_ActivatorFactory_For_Grain_Class()
    {
        const string source = """
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface IClock { }

                              public sealed class CounterGrain : Grain
                              {
                                  public CounterGrain(IClock clock) { }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainActivatorGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains("internal sealed class CounterGrainActivatorFactory", generated);
        Assert.Contains(": global::Quark.Runtime.IGrainActivatorFactory", generated);
        Assert.Contains("public global::System.Type GrainClass => typeof(global::Demo.CounterGrain);", generated);
        Assert.Contains("GetRequiredService<global::Demo.IClock>(services)", generated);
        Assert.Contains("return new global::Demo.CounterGrain(", generated);
    }

    [Fact]
    public void Generated_Factory_Create_Signature_Includes_GrainId()
    {
        const string source = """
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public sealed class SimpleGrain : Grain { }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainActivatorGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains(
            "Create(global::Quark.Core.Abstractions.Identity.GrainId grainId, global::System.IServiceProvider services)",
            generated);
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }
}
