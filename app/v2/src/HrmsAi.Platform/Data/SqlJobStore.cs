using Dapper;
using HrmsAi.Abstractions;
using HrmsAi.Platform.Jobs;
using Microsoft.Extensions.Options;

namespace HrmsAi.Platform.Data;

public class SqlJobStore(IDbConnectionFactory db, IOptions<QueueSettings> queue) : IJobStore
{
    public async Task CreateAsync(Guid id, string jobType, string? fileName, string inputText, Guid? apiKeyId)
    {
        using var c = db.Create();
        var q = queue.Value;

        if (q.MaxPendingTotal > 0)
        {
            var total = await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.AiJobs WHERE Status IN ('Queued','Running')");
            if (total >= q.MaxPendingTotal)
                throw new QueueFullException("The service is busy right now. Please try again in a few minutes.",
                                             StatusCodes.Status503ServiceUnavailable);
        }

        if (q.MaxPendingPerKey > 0)
        {
            var mine = await c.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM dbo.AiJobs
                  WHERE Status IN ('Queued','Running') AND (ApiKeyId = @apiKeyId OR (ApiKeyId IS NULL AND @apiKeyId IS NULL))",
                new { apiKeyId });
            if (mine >= q.MaxPendingPerKey)
                throw new QueueFullException(
                    $"Too many jobs are waiting for this API key ({mine}). Wait for some to finish, then submit again.");
        }

        await c.ExecuteAsync(
            @"INSERT INTO dbo.AiJobs (Id, JobType, Status, InputFileName, InputText, ApiKeyId)
              VALUES (@id, @jobType, 'Queued', @fileName, @inputText, @apiKeyId)",
            new { id, jobType, fileName, inputText, apiKeyId });
    }

    public async Task<AiJob?> GetAsync(Guid id)
    {
        using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<AiJob>("SELECT * FROM dbo.AiJobs WHERE Id = @id", new { id });
    }

    public async Task<IEnumerable<AiJob>> ListAsync(int take, Guid? apiKeyId)
    {
        using var c = db.Create();
        return await c.QueryAsync<AiJob>(
            @"SELECT TOP (@take) * FROM dbo.AiJobs
              WHERE (@apiKeyId IS NULL OR ApiKeyId = @apiKeyId)
              ORDER BY CreatedAt DESC",
            new { take, apiKeyId });
    }

    // Fair order: every client's first job, then every client's second job, and so on.
    // The UPDATE only succeeds if the job is still Queued, so two workers never take the same job.
    public async Task<AiJob?> ClaimNextAsync(string[] jobTypes)
    {
        using var c = db.Create();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var id = await c.QuerySingleOrDefaultAsync<Guid?>(
                @"WITH q AS (
                      SELECT Id, CreatedAt,
                             ROW_NUMBER() OVER (PARTITION BY ApiKeyId ORDER BY CreatedAt) AS rn
                      FROM dbo.AiJobs
                      WHERE Status = 'Queued' AND JobType IN @types)
                  SELECT TOP (1) Id FROM q ORDER BY rn, CreatedAt",
                new { types = jobTypes });
            if (id is null) return null;

            var job = await c.QuerySingleOrDefaultAsync<AiJob>(
                @"UPDATE dbo.AiJobs SET Status = 'Running', StartedAt = SYSUTCDATETIME()
                  OUTPUT inserted.*
                  WHERE Id = @id AND Status = 'Queued'",
                new { id });
            if (job is not null) return job;   // otherwise another worker took it first: pick again
        }
        return null;
    }

    // Place in the same fair order that ClaimNextAsync uses (1 = next to run)
    public async Task<int?> GetQueuePositionAsync(Guid id)
    {
        using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<int?>(
            @"WITH q AS (
                  SELECT Id, CreatedAt,
                         ROW_NUMBER() OVER (PARTITION BY ApiKeyId ORDER BY CreatedAt) AS rn
                  FROM dbo.AiJobs
                  WHERE Status = 'Queued' AND JobType = (SELECT JobType FROM dbo.AiJobs WHERE Id = @id)),
              o AS (SELECT Id, ROW_NUMBER() OVER (ORDER BY rn, CreatedAt) AS pos FROM q)
              SELECT CAST(pos AS INT) FROM o WHERE Id = @id",
            new { id });
    }

    // Average processing time of the last 20 finished jobs of this type
    public async Task<double?> AverageDurationSecondsAsync(string jobType)
    {
        using var c = db.Create();
        return await c.ExecuteScalarAsync<double?>(
            @"SELECT AVG(CAST(DurationMs AS FLOAT)) / 1000.0
              FROM (SELECT TOP (20) DurationMs FROM dbo.AiJobs
                    WHERE Status = 'Done' AND JobType = @jobType AND DurationMs IS NOT NULL
                    ORDER BY CompletedAt DESC) t",
            new { jobType });
    }

    public async Task CompleteAsync(Guid id, string resultJson, int durationMs)
    {
        using var c = db.Create();
        await c.ExecuteAsync(
            @"UPDATE dbo.AiJobs SET Status = 'Done', ResultJson = @resultJson,
                     CompletedAt = SYSUTCDATETIME(), DurationMs = @durationMs WHERE Id = @id",
            new { id, resultJson, durationMs });
    }

    public async Task FailAsync(Guid id, string error)
    {
        if (error.Length > 2000) error = error[..2000];
        using var c = db.Create();
        await c.ExecuteAsync(
            "UPDATE dbo.AiJobs SET Status = 'Failed', Error = @error, CompletedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, error });
    }

    public async Task ResetRunningAsync(string[] jobTypes)
    {
        using var c = db.Create();
        await c.ExecuteAsync(
            "UPDATE dbo.AiJobs SET Status = 'Queued', StartedAt = NULL WHERE Status = 'Running' AND JobType IN @types",
            new { types = jobTypes });
    }
}
