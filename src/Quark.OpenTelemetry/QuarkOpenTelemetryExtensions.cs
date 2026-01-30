using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Quark.OpenTelemetry;

/// <summary>
/// Extension methods for adding OpenTelemetry support to Quark.
/// </summary>
public static class QuarkOpenTelemetryExtensions
{
    /// <summary>
    /// Adds OpenTelemetry tracing for Quark actors and silos.
    /// </summary>
    /// <param name="builder">The OpenTelemetry tracer provider builder.</param>
    /// <param name="serviceName">The service name for telemetry.</param>
    /// <param name="serviceVersion">The service version for telemetry.</param>
    /// <returns>The builder for chaining.</returns>
    public static TracerProviderBuilder AddQuarkInstrumentation(
        this TracerProviderBuilder builder,
        string serviceName = "QuarkService",
        string serviceVersion = "1.0.0")
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["quark.framework.version"] = QuarkActivitySource.Version
                }))
            .AddSource(QuarkActivitySource.SourceName);
    }

    /// <summary>
    /// Adds OpenTelemetry metrics for Quark actors and silos.
    /// </summary>
    /// <param name="builder">The OpenTelemetry meter provider builder.</param>
    /// <param name="serviceName">The service name for telemetry.</param>
    /// <param name="serviceVersion">The service version for telemetry.</param>
    /// <returns>The builder for chaining.</returns>
    public static MeterProviderBuilder AddQuarkInstrumentation(
        this MeterProviderBuilder builder,
        string serviceName = "QuarkService",
        string serviceVersion = "1.0.0")
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["quark.framework.version"] = QuarkActivitySource.Version
                }))
            .AddMeter(QuarkMetrics.MeterName);
    }

    /// <summary>
    /// Adds OpenTelemetry metrics with Prometheus exporter for Quark actors and silos.
    /// This configures the Prometheus scraping endpoint at /metrics.
    /// </summary>
    /// <param name="builder">The OpenTelemetry meter provider builder.</param>
    /// <param name="serviceName">The service name for telemetry.</param>
    /// <param name="serviceVersion">The service version for telemetry.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// The Prometheus exporter automatically adds a middleware that exposes metrics at /metrics.
    /// Use this method instead of AddQuarkInstrumentation when you want Prometheus scraping support.
    /// No additional configuration is needed - the /metrics endpoint is enabled automatically.
    /// </remarks>
    public static MeterProviderBuilder AddQuarkInstrumentationWithPrometheus(
        this MeterProviderBuilder builder,
        string serviceName = "QuarkService",
        string serviceVersion = "1.0.0")
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["quark.framework.version"] = QuarkActivitySource.Version
                }))
            .AddMeter(QuarkMetrics.MeterName)
            .AddPrometheusExporter();
    }
}
