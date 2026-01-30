using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Quark.Abstractions;
using Quark.Abstractions.Clustering;
using Quark.Extensions.DependencyInjection;
using Quark.Hosting;
using Quark.Networking.Abstractions;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for Quark diagnostic endpoints including /health and /metrics.
/// </summary>
public class DiagnosticEndpointsTests
{
    [Fact]
    public async Task HealthEndpoint_ReturnsHealthReport_WhenHealthCheckServiceIsConfigured()
    {
        // Arrange
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        // Add routing services
                        services.AddRouting();
                        
                        // Add a mock silo
                        services.AddSingleton<IQuarkSilo>(new MockQuarkSilo());
                        services.AddSingleton<IQuarkClusterMembership>(new MockClusterMembership());
                        
                        // Add health checks
                        services.AddHealthChecks()
                            .AddCheck<QuarkSiloHealthCheck>("quark_silo");
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapQuarkDiagnostics("/quark");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/quark/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var healthReport = JsonSerializer.Deserialize<JsonElement>(content);
        
        Assert.True(healthReport.TryGetProperty("status", out var status));
        Assert.True(healthReport.TryGetProperty("entries", out var entries));
        Assert.True(entries.GetArrayLength() > 0);
        
        await host.StopAsync();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsNotImplemented_WhenHealthCheckServiceIsNotConfigured()
    {
        // Arrange
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        // Add routing services
                        services.AddRouting();
                        // Don't add health checks
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapQuarkDiagnostics("/quark");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/quark/health");

        // Assert
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Health check service not configured", content);
        
        await host.StopAsync();
    }

    [Fact]
    public async Task HealthEndpoint_IncludesHealthCheckData_InResponse()
    {
        // Arrange
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        // Add routing services
                        services.AddRouting();
                        
                        // Add a mock silo with active status
                        var mockSilo = new MockQuarkSilo
                        {
                            Status = SiloStatus.Active
                        };
                        services.AddSingleton<IQuarkSilo>(mockSilo);
                        services.AddSingleton<IQuarkClusterMembership>(new MockClusterMembership());
                        
                        // Add health checks
                        services.AddHealthChecks()
                            .AddCheck<QuarkSiloHealthCheck>("quark_silo");
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapQuarkDiagnostics("/quark");
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Act
        var response = await client.GetAsync("/quark/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var healthReport = JsonSerializer.Deserialize<JsonElement>(content);
        
        // Verify structure
        Assert.True(healthReport.TryGetProperty("status", out _));
        Assert.True(healthReport.TryGetProperty("totalDuration", out _));
        Assert.True(healthReport.TryGetProperty("entries", out var entries));
        
        // Verify entry data
        var entry = entries.EnumerateArray().First();
        Assert.True(entry.TryGetProperty("name", out var name));
        Assert.Equal("quark_silo", name.GetString());
        Assert.True(entry.TryGetProperty("status", out _));
        Assert.True(entry.TryGetProperty("duration", out _));
        
        await host.StopAsync();
    }

    // Mock implementations for testing
    private class MockQuarkSilo : IQuarkSilo
    {
        public string SiloId => "test-silo-1";
        public SiloStatus Status { get; set; } = SiloStatus.Active;
        public SiloInfo SiloInfo => new SiloInfo(SiloId, "localhost", 11111, Status);
        public IActorFactory ActorFactory => throw new NotImplementedException();

        public IReadOnlyCollection<IActor> GetActiveActors() => Array.Empty<IActor>();
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private class MockClusterMembership : IQuarkClusterMembership
    {
        public string CurrentSiloId => "test-silo-1";
        
        public IConsistentHashRing HashRing => throw new NotImplementedException();
        
        public event EventHandler<SiloInfo>? SiloJoined;
        public event EventHandler<SiloInfo>? SiloLeft;

        public Task<IReadOnlyCollection<SiloInfo>> GetActiveSilosAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<SiloInfo>>(new List<SiloInfo>
            {
                new SiloInfo("test-silo-1", "localhost", 11111, SiloStatus.Active)
            });
        }

        public string? GetActorSilo(string actorId, string actorType)
        {
            return "test-silo-1";
        }

        public Task<SiloInfo?> GetSiloAsync(string siloId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SiloInfo?>(new SiloInfo(siloId, "localhost", 11111, SiloStatus.Active));
        }

        public Task RegisterSiloAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UnregisterSiloAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateHeartbeatAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
