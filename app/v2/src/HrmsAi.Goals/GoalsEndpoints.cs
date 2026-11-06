using HrmsAi.Abstractions;
using HrmsAi.Compat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HrmsAi.Goals;

public static class GoalsEndpoints
{
    public static IEndpointRouteBuilder MapGoalsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/goals");
        group.MapPost("/generate", Generate).RequireQuota();                       // structured fields in, goals out
        group.MapPost("/v1/chat/completions", ChatCompletions).RequireQuota();     // Groq / OpenAI compatible
        return app;
    }

    private static LlmPolicy CompatPolicy(GoalsSettings s) =>
        new(s.Temperature, s.MaxOutputTokens, s.MaxWaitSeconds, t => GoalsJson.Normalize(t, s), ModelId: s.Model);

    private static LlmPolicy NativePolicy(GoalsSettings s) =>
        new(s.Temperature, s.MaxOutputTokens, s.MaxWaitSeconds, GoalsJson.Extract, ModelId: s.Model);

    private static Task<IResult> ChatCompletions(
        ChatCompletionRequest req, HttpContext ctx, LlmExecutor exec, IOptions<GoalsSettings> s, CancellationToken ct)
        => OpenAiCompat.HandleAsync(req, ctx, exec, "goals", "chat-completions", CompatPolicy(s.Value), ct);

    private static async Task<IResult> Generate(
        GoalRequest req, HttpContext ctx, LlmExecutor exec, IOptions<GoalsSettings> s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SummaryDescription))
            return Results.BadRequest(new { error = "summaryDescription is required." });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, GoalsPrompt.System),
            new(ChatRole.User, GoalsPrompt.Build(req))
        };
        var result = await exec.CompleteAsync(ctx, "goals", "generate", messages, NativePolicy(s.Value), ct: ct);
        if (result is null)
        {
            ctx.Response.Headers["Retry-After"] = "30";
            return Results.Json(new { error = "The AI service is busy. Please try again shortly." }, statusCode: 503);
        }
        if (!GoalsJson.TryParse(result.Text, out var goals))
            return Results.Json(new { error = "The AI did not return valid goals. Please try again." }, statusCode: 422);

        return Results.Ok(new { goals, model = result.Model, tokens = result.Usage.TotalTokens });
    }
}
