using Dapper;
using HrmsAi.Abstractions;
using HrmsAi.Access.Models;

namespace HrmsAi.Access.Data;

public class ApiKeyRepository(IDbConnectionFactory db)
{
    // Start of the current quota period (UTC) for each key, computed inside SQL
    private const string SinceApply = @"
        CROSS APPLY (SELECT CASE k.LimitPeriod
            WHEN 'Daily'   THEN CAST(CAST(SYSUTCDATETIME() AS DATE) AS DATETIME2)
            WHEN 'Monthly' THEN CAST(DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1) AS DATETIME2)
            ELSE CAST('0001-01-01' AS DATETIME2) END AS Since) p";

    private const string UsedColumn =
        "(SELECT COALESCE(SUM(CAST(u.TotalTokens AS BIGINT)), 0) FROM dbo.ApiKeyUsage u WHERE u.ApiKeyId = k.Id AND u.At >= p.Since) AS Used";

    public async Task<ApiKeyRecord?> FindByHashAsync(byte[] hash)
    {
        using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<ApiKeyRecord>("SELECT * FROM dbo.ApiKeys WHERE KeyHash = @hash", new { hash });
    }

    public async Task<ApiKeyRecord?> GetAsync(Guid id)
    {
        using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<ApiKeyRecord>("SELECT * FROM dbo.ApiKeys WHERE Id = @id", new { id });
    }

    public async Task<List<ApiKeyRow>> ListAsync()
    {
        using var c = db.Create();
        var rows = await c.QueryAsync<ApiKeyRow>(
            $"SELECT k.*, {UsedColumn} FROM dbo.ApiKeys k {SinceApply} ORDER BY k.CreatedAt DESC");
        return rows.ToList();
    }

    public async Task<QuotaState?> GetQuotaStateAsync(Guid id)
    {
        using var c = db.Create();
        return await c.QuerySingleOrDefaultAsync<QuotaState>(
            $"SELECT k.TokenLimit, k.LimitPeriod, k.IsActive, {UsedColumn} FROM dbo.ApiKeys k {SinceApply} WHERE k.Id = @id",
            new { id });
    }

    public async Task InsertAsync(ApiKeyRecord r)
    {
        using var c = db.Create();
        await c.ExecuteAsync(
            @"INSERT INTO dbo.ApiKeys (Id, ClientName, KeyPrefix, KeyHash, IsActive, AllowedModules, TokenLimit, LimitPeriod, ExpiresAt, Notes, CreatedAt)
              VALUES (@Id, @ClientName, @KeyPrefix, @KeyHash, @IsActive, @AllowedModules, @TokenLimit, @LimitPeriod, @ExpiresAt, @Notes, @CreatedAt)", r);
    }

    public async Task<bool> UpdateSettingsAsync(Guid id, string clientName, string allowedModules, long? tokenLimit,
                                                string limitPeriod, DateTime? expiresAt, string? notes)
    {
        using var c = db.Create();
        var n = await c.ExecuteAsync(
            @"UPDATE dbo.ApiKeys SET ClientName = @clientName, AllowedModules = @allowedModules, TokenLimit = @tokenLimit,
                     LimitPeriod = @limitPeriod, ExpiresAt = @expiresAt, Notes = @notes WHERE Id = @id",
            new { id, clientName, allowedModules, tokenLimit, limitPeriod, expiresAt, notes });
        return n > 0;
    }

    public async Task<bool> SetActiveAsync(Guid id, bool active)
    {
        using var c = db.Create();
        return await c.ExecuteAsync("UPDATE dbo.ApiKeys SET IsActive = @active WHERE Id = @id", new { id, active }) > 0;
    }

    public async Task<bool> ReplaceKeyAsync(Guid id, byte[] hash, string prefix)
    {
        using var c = db.Create();
        return await c.ExecuteAsync("UPDATE dbo.ApiKeys SET KeyHash = @hash, KeyPrefix = @prefix WHERE Id = @id", new { id, hash, prefix }) > 0;
    }

    public async Task InsertUsageAsync(UsageRecord r)
    {
        using var c = db.Create();
        await c.ExecuteAsync(
            @"INSERT INTO dbo.ApiKeyUsage (ApiKeyId, Module, Operation, PromptTokens, CompletionTokens, TotalTokens, JobId)
              VALUES (@ApiKeyId, @Module, @Operation, @Prompt, @Completion, @Total, @JobId);
              UPDATE dbo.ApiKeys SET LastUsedAt = SYSUTCDATETIME() WHERE Id = @ApiKeyId;",
            new { r.ApiKeyId, r.Module, r.Operation, Prompt = r.Usage.PromptTokens, Completion = r.Usage.CompletionTokens, Total = r.Usage.TotalTokens, r.JobId });
    }

    public async Task<IEnumerable<UsageDay>> UsageByDayAsync(Guid id, int days)
    {
        using var c = db.Create();
        return await c.QueryAsync<UsageDay>(
            @"SELECT CAST(At AS DATE) AS [Day], SUM(CAST(TotalTokens AS BIGINT)) AS Tokens, COUNT(*) AS Requests
              FROM dbo.ApiKeyUsage
              WHERE ApiKeyId = @id AND At >= DATEADD(DAY, -@days, SYSUTCDATETIME())
              GROUP BY CAST(At AS DATE) ORDER BY [Day] DESC", new { id, days });
    }

    public async Task<IEnumerable<UsageRow>> RecentUsageAsync(Guid id, int take)
    {
        using var c = db.Create();
        return await c.QueryAsync<UsageRow>(
            @"SELECT TOP (@take) At, Module, Operation, PromptTokens, CompletionTokens, TotalTokens
              FROM dbo.ApiKeyUsage WHERE ApiKeyId = @id ORDER BY Id DESC", new { id, take });
    }
}
