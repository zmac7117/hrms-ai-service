namespace HrmsAi.Abstractions;

public record JobOutcome(string ResultJson, AiUsage Usage);

// A module implements one of these for each job type it owns
public interface IJobHandler
{
    string Module { get; }
    string JobType { get; }
    Task<JobOutcome> HandleAsync(AiJob job, CancellationToken ct);
}

public interface IJobStore
{
    Task CreateAsync(Guid id, string jobType, string? fileName, string inputText, Guid? apiKeyId);
    Task<AiJob?> GetAsync(Guid id);
    Task<IEnumerable<AiJob>> ListAsync(int take, Guid? apiKeyId);
    Task<AiJob?> ClaimNextAsync(string[] jobTypes);
    Task CompleteAsync(Guid id, string resultJson, int durationMs);
    Task FailAsync(Guid id, string error);
    Task ResetRunningAsync(string[] jobTypes);
    Task<int?> GetQueuePositionAsync(Guid id);
    Task<double?> AverageDurationSecondsAsync(string jobType);
}

