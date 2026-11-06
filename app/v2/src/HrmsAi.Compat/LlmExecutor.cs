using System.Diagnostics;
using HrmsAi.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HrmsAi.Compat;

// How one feature wants its AI calls handled
public record LlmPolicy(
    double Temperature,
    int MaxOutputTokens,
    int MaxWaitSeconds,
    Func<string, string>? PostProcess = null,
    bool HonorJsonMode = false,
    string? ModelId = null);

public record LlmResult(string Text, AiUsage Usage, string Model);

// Runs one prompt: waits for an AI slot, calls the model, cleans the text,
// and records tokens, time, a short description and the result for the dashboard.
public class LlmExecutor(IChatClient chat, IAiGate gate, IUsageRecorder usage, IOptions<AiOptions> ai)
{
    // Returns null when no AI slot became free in time (the caller answers 503).
    public async Task<LlmResult?> CompleteAsync(
        HttpContext http, string module, string operation,
        IReadOnlyList<ChatMessage> messages, LlmPolicy policy,
        double? temperature = null, int? maxTokens = null, bool wantJson = false,
        CancellationToken ct = default)
    {
        using var slot = await gate.AcquireAsync(TimeSpan.FromSeconds(policy.MaxWaitSeconds), ct);
        if (slot is null) return null;

        var keyId = http.GetCaller()?.KeyId;
        var summary = PromptSummary.Describe(messages);
        var sw = Stopwatch.StartNew();
        try
        {
            var options = new ChatOptions
            {
                Temperature = (float)(temperature ?? policy.Temperature),
                MaxOutputTokens = Math.Min(maxTokens ?? policy.MaxOutputTokens, policy.MaxOutputTokens)
            };
            if (!string.IsNullOrWhiteSpace(policy.ModelId)) options.ModelId = policy.ModelId;
            if (wantJson && policy.HonorJsonMode) options.ResponseFormat = ChatResponseFormat.Json;

            var response = await chat.GetResponseAsync(messages, options, ct);
            var text = response.Text ?? "";
            if (policy.PostProcess is not null) text = policy.PostProcess(text);

            var promptChars = messages.Sum(m => m.Text?.Length ?? 0);
            var tokens = AiUsage.From(response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, promptChars);
            await usage.RecordAsync(new UsageRecord(keyId, module, operation, tokens, null,
                DurationMs: (int)sw.ElapsedMilliseconds, InputSummary: summary, ResultText: text, Status: "Done"), ct);

            return new LlmResult(text, tokens, policy.ModelId ?? ai.Value.ChatModel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                await usage.RecordAsync(new UsageRecord(keyId, module, operation, AiUsage.None, null,
                    DurationMs: (int)sw.ElapsedMilliseconds, InputSummary: summary, Error: ex.Message, Status: "Failed"), CancellationToken.None);
            }
            catch { /* history must never hide the real error */ }
            throw;
        }
    }
}
