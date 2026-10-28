namespace HrmsAi.Abstractions;

public record AiUsage(int PromptTokens, int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
    public static AiUsage None { get; } = new(0, 0);

    // Uses the token counts reported by the model; estimates roughly if it reported nothing.
    public static AiUsage From(long? inputTokens, long? outputTokens, int fallbackChars) =>
        inputTokens is null && outputTokens is null
            ? new AiUsage(Math.Max(1, fallbackChars / 4), 300)
            : new AiUsage((int)(inputTokens ?? 0), (int)(outputTokens ?? 0));
}

public record UsageRecord(Guid? ApiKeyId, string Module, string Operation, AiUsage Usage, Guid? JobId);

public interface IUsageRecorder
{
    Task RecordAsync(UsageRecord record, CancellationToken ct);
}
