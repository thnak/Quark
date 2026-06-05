namespace Quark.Core.Hosting;

/// <summary>
///     Extension methods for <see cref="ISiloBuilder" />.
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    ///     Enables OpenTelemetry <see cref="System.Diagnostics.Activity" /> propagation
    ///     across grain calls. The Quark ActivitySource name is <c>"Quark.Runtime"</c>.
    ///     Drop-in equivalent of Orleans' <c>AddActivityPropagation()</c>.
    /// </summary>
    public static ISiloBuilder AddActivityPropagation(this ISiloBuilder builder)
    {
        // ActivitySource in LocalGrainCallInvoker is always-on.
        // StartActivity returns null when no listener is attached (zero overhead).
        return builder;
    }
}

