using HrmsAi.Abstractions;
using HrmsAi.Access.Data;

namespace HrmsAi.Access.Services;

public class DbUsageRecorder(ApiKeyRepository repo) : IUsageRecorder
{
    public async Task RecordAsync(UsageRecord record, CancellationToken ct)
    {
        if (record.ApiKeyId is null) return;   // the legacy shared key is not metered
        await repo.InsertUsageAsync(record);
    }
}
