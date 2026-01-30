using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Quark.Hosting;
using System.Text.Json;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for mapping Quark diagnostic endpoints.
/// </summary>
public static class QuarkDiagnosticEndpoints
{
    /// <summary>
    /// Maps Quark diagnostic endpoints for monitoring and troubleshooting.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The base path pattern for diagnostic endpoints. Defaults to "/quark".</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapQuarkDiagnostics(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/quark")
    {
        if (endpoints == null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        var basePath = pattern.TrimEnd('/');

        // GET /quark/actors - List all active actors
        endpoints.MapGet($"{basePath}/actors", async (HttpContext context) =>
        {
            var silo = context.RequestServices.GetService<IQuarkSilo>();
            if (silo == null)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Silo not available"
                });
                return;
            }

            var actors = silo.GetActiveActors();
            var actorList = actors.Select(actor => new
            {
                actorId = actor.ActorId,
                actorType = actor.GetType().Name,
                fullTypeName = actor.GetType().FullName
            }).ToList();

            await context.Response.WriteAsJsonAsync(new
            {
                siloId = silo.SiloId,
                activeActorCount = actors.Count,
                actors = actorList
            }, new JsonSerializerOptions { WriteIndented = true });
        })
        .WithName("GetActiveActors")
        .WithTags("Quark Diagnostics");

        // GET /quark/cluster - View cluster membership
        endpoints.MapGet($"{basePath}/cluster", async (HttpContext context) =>
        {
            var silo = context.RequestServices.GetService<IQuarkSilo>();
            var membership = context.RequestServices.GetService<Quark.Networking.Abstractions.IQuarkClusterMembership>();
            
            if (silo == null || membership == null)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Silo or cluster membership not available"
                });
                return;
            }

            try
            {
                var silos = await membership.GetActiveSilosAsync();
                var siloList = silos.Select(s => new
                {
                    siloId = s.SiloId,
                    address = s.Address,
                    port = s.Port,
                    status = s.Status.ToString(),
                    lastHeartbeat = s.LastHeartbeat
                }).ToList();

                await context.Response.WriteAsJsonAsync(new
                {
                    currentSiloId = silo.SiloId,
                    clusterSize = silos.Count,
                    silos = siloList
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Failed to retrieve cluster information",
                    message = ex.Message
                });
            }
        })
        .WithName("GetClusterInfo")
        .WithTags("Quark Diagnostics");

        // GET /quark/config - View current silo configuration (sanitized)
        endpoints.MapGet($"{basePath}/config", async (HttpContext context) =>
        {
            var silo = context.RequestServices.GetService<IQuarkSilo>();
            if (silo == null)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Silo not available"
                });
                return;
            }

            // Return sanitized configuration (no connection strings or secrets)
            await context.Response.WriteAsJsonAsync(new
            {
                siloId = silo.SiloId,
                status = silo.Status.ToString(),
                activeActors = silo.GetActiveActors().Count,
                // Add more configuration details as needed
                // Note: Ensure no sensitive data (connection strings, secrets) is exposed
            }, new JsonSerializerOptions { WriteIndented = true });
        })
        .WithName("GetSiloConfig")
        .WithTags("Quark Diagnostics");

        // GET /quark/status - Quick status check
        endpoints.MapGet($"{basePath}/status", async (HttpContext context) =>
        {
            var silo = context.RequestServices.GetService<IQuarkSilo>();
            if (silo == null)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "unavailable",
                    message = "Silo not available"
                });
                return;
            }

            await context.Response.WriteAsJsonAsync(new
            {
                siloId = silo.SiloId,
                status = silo.Status.ToString().ToLowerInvariant(),
                activeActors = silo.GetActiveActors().Count,
                timestamp = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true });
        })
        .WithName("GetSiloStatus")
        .WithTags("Quark Diagnostics");

        // GET /quark/dlq - View dead letter queue messages
        endpoints.MapGet($"{basePath}/dlq", async (HttpContext context) =>
        {
            var dlq = context.RequestServices.GetService<Quark.Abstractions.IDeadLetterQueue>();
            if (dlq == null)
            {
                context.Response.StatusCode = 501; // Not Implemented
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Dead Letter Queue not configured"
                });
                return;
            }

            var actorIdFilter = context.Request.Query["actorId"].ToString();
            var messages = string.IsNullOrEmpty(actorIdFilter)
                ? await dlq.GetAllAsync()
                : await dlq.GetByActorAsync(actorIdFilter);

            await context.Response.WriteAsJsonAsync(new
            {
                totalMessages = dlq.MessageCount,
                filteredCount = messages.Count,
                messages = messages.Select(m => new
                {
                    messageId = m.Message.MessageId,
                    actorId = m.ActorId,
                    enqueuedAt = m.EnqueuedAt,
                    retryCount = m.RetryCount,
                    errorType = m.Exception.GetType().Name,
                    errorMessage = m.Exception.Message,
                    correlationId = m.Message.CorrelationId
                })
            }, new JsonSerializerOptions { WriteIndented = true });
        })
        .WithName("GetDeadLetterQueue")
        .WithTags("Quark Diagnostics");

        // DELETE /quark/dlq - Clear all dead letter messages
        endpoints.MapDelete($"{basePath}/dlq", async (HttpContext context) =>
        {
            var dlq = context.RequestServices.GetService<Quark.Abstractions.IDeadLetterQueue>();
            if (dlq == null)
            {
                context.Response.StatusCode = 501; // Not Implemented
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Dead Letter Queue not configured"
                });
                return;
            }

            await dlq.ClearAsync();

            await context.Response.WriteAsJsonAsync(new
            {
                message = "Dead Letter Queue cleared successfully"
            });
        })
        .WithName("ClearDeadLetterQueue")
        .WithTags("Quark Diagnostics");

        // DELETE /quark/dlq/{messageId} - Remove specific dead letter message
        endpoints.MapDelete($"{basePath}/dlq/{{messageId}}", async (HttpContext context, string messageId) =>
        {
            var dlq = context.RequestServices.GetService<Quark.Abstractions.IDeadLetterQueue>();
            if (dlq == null)
            {
                context.Response.StatusCode = 501; // Not Implemented
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Dead Letter Queue not configured"
                });
                return;
            }

            // Validate messageId
            if (string.IsNullOrWhiteSpace(messageId))
            {
                context.Response.StatusCode = 400; // Bad Request
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Message ID cannot be empty"
                });
                return;
            }

            var removed = await dlq.RemoveAsync(messageId);

            if (removed)
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Message removed successfully"
                });
            }
            else
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Message not found"
                });
            }
        })
        .WithName("RemoveDeadLetterMessage")
        .WithTags("Quark Diagnostics");

        return endpoints;
    }
}
