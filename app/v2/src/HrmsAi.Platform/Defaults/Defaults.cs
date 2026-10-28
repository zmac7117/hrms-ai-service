using HrmsAi.Abstractions;

namespace HrmsAi.Platform.Defaults;

// Phase 1 defaults: no limits, no usage tracking. Phase 2 replaces both.
public class AllowAllQuotaGate : IQuotaGate
{
    public Task<QuotaResult> CheckAsync(CallerInfo? caller, CancellationToken ct) => Task.FromResult(QuotaResult.Allow);
}

public class NoOpUsageRecorder : IUsageRecorder
{
    public Task RecordAsync(UsageRecord record, CancellationToken ct) => Task.CompletedTask;
}
