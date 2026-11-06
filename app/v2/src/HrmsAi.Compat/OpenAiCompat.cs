using HrmsAi.Abstractions;
using Microsoft.Extensions.AI;

namespace HrmsAi.Compat;

// Answers a Groq / OpenAI style "chat completions" call using our own model
public static class OpenAiCompat
{
    public static async Task<IResult> HandleAsync(
        ChatCompletionRequest req, HttpContext http, LlmExecutor exec,
        string module, string operation, LlmPolicy policy, CancellationToken ct)
    {
        if (req.Messages.Count == 0) return Error(400, "'messages' is required.", "invalid_request_error");
        if (req.Stream == true) return Error(400, "Streaming is not supported by this service.", "invalid_request_error");

        var messages = req.Messages
            .Select(m => new ChatMessage(MapRole(m.Role), m.Content ?? ""))
            .ToList();

        var result = await exec.CompleteAsync(
            http, module, operation, messages, policy,
            req.Temperature, req.MaxCompletionTokens ?? req.MaxTokens,
            req.ResponseFormat?.Type == "json_object", ct);

        if (result is null)
        {
            http.Response.Headers["Retry-After"] = "30";
            return Error(503, "The AI service is busy. Please try again shortly.", "server_error");
        }

        var body = new ChatCompletionResponse
        {
            Id = "chatcmpl-" + Guid.NewGuid().ToString("N"),
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = result.Model,
            Choices =
            {
                new ChatCompletionChoice
                {
                    Index = 0,
                    FinishReason = "stop",
                    Message = new ChatCompletionMessage { Role = "assistant", Content = result.Text }
                }
            },
            Usage = new ChatCompletionUsage
            {
                PromptTokens = result.Usage.PromptTokens,
                CompletionTokens = result.Usage.CompletionTokens,
                TotalTokens = result.Usage.TotalTokens
            }
        };
        return Results.Json(body);
    }

    private static ChatRole MapRole(string? role) => (role ?? "user").ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.User
    };

    private static IResult Error(int status, string message, string type) =>
        Results.Json(new { error = new { message, type } }, statusCode: status);
}
