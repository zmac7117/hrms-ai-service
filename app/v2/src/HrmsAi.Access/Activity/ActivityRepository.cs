using Dapper;
using HrmsAi.Abstractions;

namespace HrmsAi.Access.Activity;

// Everything the dashboards read, and the usage history written after each AI request.
// Asynchronous work (resume files, match scores) comes from the jobs table; direct calls come from the usage table.
public class ActivityRepository(IDbConnectionFactory db)
{
    private const string Combined = @"
        SELECT CONCAT('job:', CONVERT(NVARCHAR(36), j.Id)) AS Id, 'job' AS Source, j.CreatedAt AS At, j.JobType AS Module,
               CASE j.JobType WHEN 'resume' THEN 'parse-file' WHEN 'match' THEN 'match-score' ELSE j.JobType END AS Operation,
               j.Status AS Status, j.DurationMs AS DurationMs, j.InputFileName AS Title,
               COALESCE(u.PromptTokens, 0) AS PromptTokens, COALESCE(u.CompletionTokens, 0) AS CompletionTokens,
               COALESCE(u.TotalTokens, 0) AS TotalTokens, j.Error AS Error, j.ApiKeyId AS ApiKeyId
        FROM dbo.AiJobs j
        LEFT JOIN dbo.ApiKeyUsage u ON u.JobId = j.Id
        WHERE j.ApiKeyId IS NOT NULL AND j.CreatedAt >= @since AND (@keyId IS NULL OR j.ApiKeyId = @keyId)
        UNION ALL
        SELECT CONCAT('call:', CONVERT(NVARCHAR(20), u.Id)), 'call', u.At, u.Module, u.Operation,
               COALESCE(u.Status, 'Done'), u.DurationMs, u.InputSummary,
               u.PromptTokens, u.CompletionTokens, u.TotalTokens, u.Error, u.ApiKeyId
        FROM dbo.ApiKeyUsage u
        WHERE u.JobId IS NULL AND u.At >= @since AND (@keyId IS NULL OR u.ApiKeyId = @keyId)";

    private static DateTime Since(int days) => days > 0 ? DateTime.UtcNow.AddDays(-days) : DateTime.MinValue;
    private static string? Cap(string? s, int max) => s is null ? null : (s.Length <= max ? s : s.Substring(0, max));

    public async Task InsertUsageAsync(UsageRecord r)
    {
        using var c = db.Create();
        await c.ExecuteAsync(
            @"INSERT INTO dbo.ApiKeyUsage
                  (ApiKeyId, Module, Operation, PromptTokens, CompletionTokens, TotalTokens, JobId, DurationMs, Status, InputSummary, ResultText, Error)
              VALUES (@ApiKeyId, @Module, @Operation, @Prompt, @Completion, @Total, @JobId, @DurationMs, @Status, @InputSummary, @ResultText, @Error);
              UPDATE dbo.ApiKeys SET LastUsedAt = SYSUTCDATETIME() WHERE Id = @ApiKeyId;",
            new
            {
                r.ApiKeyId, r.Module, r.Operation,
                Prompt = r.Usage.PromptTokens, Completion = r.Usage.CompletionTokens, Total = r.Usage.TotalTokens,
                r.JobId, r.DurationMs,
                Status = r.Status ?? "Done",
                InputSummary = Cap(r.InputSummary, 300),
                ResultText = Cap(r.ResultText, 200000),
                Error = Cap(r.Error, 1000)
            });
    }

    public async Task<(List<ActivityRow> Items, bool HasMore)> ListAsync(
        Guid? keyId, int days, string? module, string? status, int skip, int take)
    {
        using var c = db.Create();
        var rows = (await c.QueryAsync<ActivityRow>($@"
            SELECT x.*, k.ClientName
            FROM ({Combined}) x
            LEFT JOIN dbo.ApiKeys k ON k.Id = x.ApiKeyId
            WHERE (@module IS NULL OR x.Module = @module) AND (@status IS NULL OR x.Status = @status)
            ORDER BY x.At DESC
            OFFSET @skip ROWS FETCH NEXT @fetch ROWS ONLY",
            new { since = Since(days), keyId, module, status, skip, fetch = take + 1 })).ToList();

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return (rows, hasMore);
    }

    public async Task<ActivitySummary> SummaryAsync(Guid? keyId, int days)
    {
        using var c = db.Create();
        using var multi = await c.QueryMultipleAsync($@"
            SELECT x.Module AS Module, COUNT(*) AS Requests,
                   COALESCE(SUM(CAST(x.TotalTokens AS BIGINT)), 0) AS Tokens,
                   SUM(CASE WHEN x.Status = 'Failed' THEN 1 ELSE 0 END) AS Failed,
                   AVG(CASE WHEN x.DurationMs IS NOT NULL THEN CAST(x.DurationMs AS FLOAT) END) / 1000.0 AS AvgSeconds
            FROM ({Combined}) x GROUP BY x.Module ORDER BY COUNT(*) DESC;

            SELECT CAST(x.At AS DATE) AS [Day], COUNT(*) AS Requests,
                   COALESCE(SUM(CAST(x.TotalTokens AS BIGINT)), 0) AS Tokens
            FROM ({Combined}) x GROUP BY CAST(x.At AS DATE) ORDER BY [Day];

            SELECT AVG(CASE WHEN x.DurationMs IS NOT NULL THEN CAST(x.DurationMs AS FLOAT) END) / 1000.0
            FROM ({Combined}) x;",
            new { since = Since(days), keyId });

        var modules = (await multi.ReadAsync<ModuleStat>()).ToList();
        var byDay = (await multi.ReadAsync<DayStat>()).ToList();
        var avg = await multi.ReadSingleAsync<double?>();

        return new ActivitySummary
        {
            Days = days,
            Requests = modules.Sum(m => m.Requests),
            Tokens = modules.Sum(m => m.Tokens),
            Failed = modules.Sum(m => m.Failed),
            AvgSeconds = avg,
            ByModule = modules,
            ByDay = byDay
        };
    }

    // keyId set = the caller may only open its own requests
    public async Task<ActivityDetail?> DetailAsync(string id, Guid? keyId)
    {
        using var c = db.Create();

        if (id.StartsWith("job:") && Guid.TryParse(id.AsSpan(4), out var jobId))
            return await c.QueryFirstOrDefaultAsync<ActivityDetail>(@"
                SELECT CONCAT('job:', CONVERT(NVARCHAR(36), j.Id)) AS Id, 'job' AS Source, j.CreatedAt AS At, j.CompletedAt AS CompletedAt,
                       j.JobType AS Module,
                       CASE j.JobType WHEN 'resume' THEN 'parse-file' WHEN 'match' THEN 'match-score' ELSE j.JobType END AS Operation,
                       j.Status AS Status, j.DurationMs AS DurationMs, j.InputFileName AS Title,
                       COALESCE(u.PromptTokens, 0) AS PromptTokens, COALESCE(u.CompletionTokens, 0) AS CompletionTokens,
                       COALESCE(u.TotalTokens, 0) AS TotalTokens, j.Error AS Error, j.ApiKeyId AS ApiKeyId,
                       k.ClientName AS ClientName, j.ResultJson AS Result
                FROM dbo.AiJobs j
                LEFT JOIN dbo.ApiKeyUsage u ON u.JobId = j.Id
                LEFT JOIN dbo.ApiKeys k ON k.Id = j.ApiKeyId
                WHERE j.Id = @jobId AND j.ApiKeyId IS NOT NULL AND (@keyId IS NULL OR j.ApiKeyId = @keyId)",
                new { jobId, keyId });

        if (id.StartsWith("call:") && long.TryParse(id.AsSpan(5), out var callId))
            return await c.QueryFirstOrDefaultAsync<ActivityDetail>(@"
                SELECT CONCAT('call:', CONVERT(NVARCHAR(20), u.Id)) AS Id, 'call' AS Source, u.At AS At,
                       DATEADD(MILLISECOND, COALESCE(u.DurationMs, 0), u.At) AS CompletedAt,
                       u.Module AS Module, u.Operation AS Operation, COALESCE(u.Status, 'Done') AS Status,
                       u.DurationMs AS DurationMs, u.InputSummary AS Title,
                       u.PromptTokens AS PromptTokens, u.CompletionTokens AS CompletionTokens, u.TotalTokens AS TotalTokens,
                       u.Error AS Error, u.ApiKeyId AS ApiKeyId, k.ClientName AS ClientName, u.ResultText AS Result
                FROM dbo.ApiKeyUsage u
                LEFT JOIN dbo.ApiKeys k ON k.Id = u.ApiKeyId
                WHERE u.Id = @callId AND u.JobId IS NULL AND (@keyId IS NULL OR u.ApiKeyId = @keyId)",
                new { callId, keyId });

        return null;
    }
}
