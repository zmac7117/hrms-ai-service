using HrmsAi.Abstractions;
using Microsoft.Extensions.Configuration;

namespace HrmsAi.Access.Activity;

// Replaces the plain usage recorder: stores tokens plus time, description, result and error
public class ActivityUsageRecorder(ActivityRepository repo, IConfiguration config) : IUsageRecorder
{
    public async Task RecordAsync(UsageRecord record, CancellationToken ct)
    {
        if (record.ApiKeyId is null) return;   // the old shared key has no history

        // Set Activity:StoreResults to false if results of direct calls must not be kept
        var keepResults = config.GetValue<bool?>("Activity:StoreResults") ?? true;
        await repo.InsertUsageAsync(keepResults ? record : record with { ResultText = null });
    }
}
