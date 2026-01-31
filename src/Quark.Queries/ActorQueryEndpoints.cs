using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Quark.Queries;

/// <summary>
/// Extension methods for mapping actor query diagnostic endpoints.
/// </summary>
public static class ActorQueryEndpoints
{
    /// <summary>
    /// Maps actor query endpoints for querying and analytics.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The base path pattern. Defaults to "/quark/actors".</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapActorQueryEndpoints(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/quark/actors")
    {
        if (endpoints == null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        var basePath = pattern.TrimEnd('/');

        // GET /quark/actors/query - Query actors with filters
        endpoints.MapGet($"{basePath}/query", async (HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<IActorQueryService>();
            if (queryService == null)
            {
                context.Response.StatusCode = 501;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Actor query service not configured. Call AddActorQueries() on the service collection."
                });
                return;
            }

            try
            {
                var typeFilter = context.Request.Query["type"].ToString();
                var idPattern = context.Request.Query["idPattern"].ToString();
                var pageNumber = int.TryParse(context.Request.Query["page"], out var p) ? p : 1;
                var pageSize = int.TryParse(context.Request.Query["pageSize"], out var ps) ? ps : 100;

                // Build predicate based on query parameters
                Func<ActorMetadata, bool> predicate = metadata => true;

                if (!string.IsNullOrEmpty(typeFilter))
                {
                    var originalPredicate = predicate;
                    predicate = metadata => originalPredicate(metadata) && metadata.ActorType.Equals(typeFilter, StringComparison.OrdinalIgnoreCase);
                }

                if (!string.IsNullOrEmpty(idPattern))
                {
                    var originalPredicate = predicate;
                    var regex = System.Text.RegularExpressions.Regex.Escape(idPattern)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".");
                    var pattern_regex = new System.Text.RegularExpressions.Regex($"^{regex}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    predicate = metadata => originalPredicate(metadata) && pattern_regex.IsMatch(metadata.ActorId);
                }

                var result = await queryService.QueryActorMetadataAsync(predicate, pageNumber, pageSize, context.RequestAborted);

                await context.Response.WriteAsJsonAsync(new
                {
                    items = result.Items,
                    totalCount = result.TotalCount,
                    pageNumber = result.PageNumber,
                    pageSize = result.PageSize,
                    totalPages = result.TotalPages,
                    hasNextPage = result.HasNextPage,
                    hasPreviousPage = result.HasPreviousPage
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Failed to query actors",
                    message = ex.Message
                });
            }
        })
        .WithName("QueryActors")
        .WithTags("Quark Actor Queries");

        // GET /quark/actors/stats - Get aggregate statistics
        endpoints.MapGet($"{basePath}/stats", async (HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<IActorQueryService>();
            if (queryService == null)
            {
                context.Response.StatusCode = 501;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Actor query service not configured"
                });
                return;
            }

            try
            {
                var totalCount = await queryService.CountActorsAsync(context.RequestAborted);
                var byType = await queryService.GroupActorsByTypeAsync(context.RequestAborted);

                await context.Response.WriteAsJsonAsync(new
                {
                    totalActors = totalCount,
                    actorsByType = byType,
                    typeCount = byType.Count
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Failed to get actor statistics",
                    message = ex.Message
                });
            }
        })
        .WithName("GetActorStats")
        .WithTags("Quark Actor Queries");

        // GET /quark/actors/types - Get list of all actor types
        endpoints.MapGet($"{basePath}/types", async (HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<IActorQueryService>();
            if (queryService == null)
            {
                context.Response.StatusCode = 501;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Actor query service not configured"
                });
                return;
            }

            try
            {
                var byType = await queryService.GroupActorsByTypeAsync(context.RequestAborted);
                var types = byType.Select(kvp => new
                {
                    typeName = kvp.Key,
                    count = kvp.Value
                }).OrderByDescending(t => t.count).ToList();

                await context.Response.WriteAsJsonAsync(new
                {
                    totalTypes = types.Count,
                    types = types
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Failed to get actor types",
                    message = ex.Message
                });
            }
        })
        .WithName("GetActorTypes")
        .WithTags("Quark Actor Queries");

        // GET /quark/actors/count - Get total actor count
        endpoints.MapGet($"{basePath}/count", async (HttpContext context) =>
        {
            var queryService = context.RequestServices.GetService<IActorQueryService>();
            if (queryService == null)
            {
                context.Response.StatusCode = 501;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Actor query service not configured"
                });
                return;
            }

            try
            {
                var typeFilter = context.Request.Query["type"].ToString();
                int count;

                if (string.IsNullOrEmpty(typeFilter))
                {
                    count = await queryService.CountActorsAsync(context.RequestAborted);
                }
                else
                {
                    count = await queryService.CountActorsAsync(
                        m => m.ActorType.Equals(typeFilter, StringComparison.OrdinalIgnoreCase),
                        context.RequestAborted);
                }

                await context.Response.WriteAsJsonAsync(new
                {
                    count = count,
                    filter = string.IsNullOrEmpty(typeFilter) ? "none" : $"type={typeFilter}"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Failed to count actors",
                    message = ex.Message
                });
            }
        })
        .WithName("CountActors")
        .WithTags("Quark Actor Queries");

        return endpoints;
    }
}
