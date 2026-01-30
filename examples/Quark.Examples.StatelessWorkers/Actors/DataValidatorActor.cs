using Quark.Abstractions;
using Quark.Core.Actors;
using System.Text.Json;

namespace Quark.Examples.StatelessWorkers.Actors;

/// <summary>
/// Stateless worker for data validation and enrichment.
/// Demonstrates high-throughput stateless processing for API requests.
/// </summary>
[Actor(Name = "DataValidator", Stateless = true)]
[StatelessWorker(MinInstances = 5, MaxInstances = 50)]
public class DataValidatorActor : StatelessActorBase
{
    public DataValidatorActor(string actorId) : base(actorId)
    {
    }

    public DataValidatorActor(string actorId, IActorFactory? actorFactory) : base(actorId, actorFactory)
    {
    }

    /// <summary>
    /// Validates and enriches user data (stateless computation).
    /// </summary>
    public async Task<EnrichedData> EnrichUserDataAsync(UserData userData)
    {
        // Simulate data validation
        await Task.Delay(10);

        var validationErrors = new List<string>();

        if (string.IsNullOrWhiteSpace(userData.Email))
            validationErrors.Add("Email is required");
        else if (!IsValidEmail(userData.Email))
            validationErrors.Add("Invalid email format");

        if (string.IsNullOrWhiteSpace(userData.Name))
            validationErrors.Add("Name is required");

        // Simulate data enrichment (e.g., from external API)
        var enrichedData = new EnrichedData
        {
            OriginalData = userData,
            IsValid = validationErrors.Count == 0,
            ValidationErrors = validationErrors,
            EnrichedAt = DateTime.UtcNow,
            ProcessedBy = ActorId,
            Metadata = new Dictionary<string, string>
            {
                ["email_domain"] = GetEmailDomain(userData.Email),
                ["name_length"] = userData.Name?.Length.ToString() ?? "0",
                ["processor"] = "QuarkStatelessWorker"
            }
        };

        return enrichedData;
    }

    /// <summary>
    /// Validates a batch of records (stateless computation).
    /// </summary>
    public async Task<BatchValidationResult> ValidateBatchAsync(List<UserData> batch)
    {
        // Process batch in parallel (stateless workers can handle this efficiently)
        var tasks = batch.Select(data => EnrichUserDataAsync(data));
        var results = await Task.WhenAll(tasks);

        return new BatchValidationResult
        {
            TotalRecords = batch.Count,
            ValidRecords = results.Count(r => r.IsValid),
            InvalidRecords = results.Count(r => !r.IsValid),
            Results = results.ToList(),
            ProcessedBy = ActorId,
            ProcessedAt = DateTime.UtcNow
        };
    }

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        return email.Contains('@') && email.Contains('.');
    }

    private static string GetEmailDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "unknown";

        var parts = email.Split('@');
        return parts.Length > 1 ? parts[1] : "unknown";
    }
}

/// <summary>
/// Input data for validation.
/// </summary>
public record UserData
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public int Age { get; init; }
}

/// <summary>
/// Enriched and validated data.
/// </summary>
public record EnrichedData
{
    public UserData? OriginalData { get; init; }
    public bool IsValid { get; init; }
    public List<string> ValidationErrors { get; init; } = new();
    public DateTime EnrichedAt { get; init; }
    public string ProcessedBy { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Result of batch validation.
/// </summary>
public record BatchValidationResult
{
    public int TotalRecords { get; init; }
    public int ValidRecords { get; init; }
    public int InvalidRecords { get; init; }
    public List<EnrichedData> Results { get; init; } = new();
    public string ProcessedBy { get; init; } = string.Empty;
    public DateTime ProcessedAt { get; init; }
}
