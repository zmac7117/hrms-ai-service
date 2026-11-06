using HrmsAi.Abstractions;
using HrmsAi.Compat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HrmsAi.JobDescription;

public static class JdEndpoints
{
    public static IEndpointRouteBuilder MapJdEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jd");
        group.MapPost("/generate", Generate).RequireQuota();                       // structured fields in, HTML out
        group.MapPost("/v1/chat/completions", ChatCompletions).RequireQuota();     // Groq / OpenAI compatible
        return app;
    }

    private static LlmPolicy Policy(JdSettings s) =>
        new(s.Temperature, s.MaxOutputTokens, s.MaxWaitSeconds, HtmlCleaner.Clean, ModelId: s.Model);

    private static Task<IResult> ChatCompletions(
        ChatCompletionRequest req, HttpContext ctx, LlmExecutor exec, IOptions<JdSettings> s, CancellationToken ct)
        => OpenAiCompat.HandleAsync(req, ctx, exec, "jd", "chat-completions", Policy(s.Value), ct);

    private static async Task<IResult> Generate(
        JdRequest req, HttpContext ctx, LlmExecutor exec, IOptions<JdSettings> s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.JobTitle))
            return Results.BadRequest(new { error = "jobTitle is required." });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, JdPrompt.System),
            new(ChatRole.User, JdPrompt.Build(req))
        };
        var result = await exec.CompleteAsync(ctx, "jd", "generate", messages, Policy(s.Value), ct: ct);
        if (result is null)
        {
            ctx.Response.Headers["Retry-After"] = "30";
            return Results.Json(new { error = "The AI service is busy. Please try again shortly." }, statusCode: 503);
        }
        return Results.Ok(new { html = result.Text, model = result.Model, tokens = result.Usage.TotalTokens });
    }
}
