using HrmsAi.Abstractions;
using HrmsAi.Access.Data;

namespace HrmsAi.Access.Services;

public class DbQuotaGate(ApiKeyRepository repo) : IQuotaGate
{
    public async Task<QuotaResult> CheckAsync(CallerInfo? caller, CancellationToken ct)
    {
        if (caller?.KeyId is null) return QuotaResult.Allow;   // legacy key: no limit

        var s = await repo.GetQuotaStateAsync(caller.KeyId.Value);
        if (s is null || s.TokenLimit is null) return QuotaResult.Allow;

        if (s.Used >= s.TokenLimit.Value)
            return QuotaResult.Deny(
                $"Token limit reached for this API key ({s.Used:N0} of {s.TokenLimit.Value:N0} tokens {PeriodText(s.LimitPeriod)}). Please contact the administrator.");

        return QuotaResult.Allow;
    }

    private static string PeriodText(string period) => period switch
    {
        "Daily"   => "today",
        "Monthly" => "this month",
        _         => "in total"
    };
}
