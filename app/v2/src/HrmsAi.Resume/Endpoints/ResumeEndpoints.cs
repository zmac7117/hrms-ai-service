using System.Diagnostics;
using HrmsAi.Abstractions;
using HrmsAi.Resume.Models;
using HrmsAi.Resume.Services;

namespace HrmsAi.Resume.Endpoints;

public static class ResumeEndpoints
{
    public static IEndpointRouteBuilder MapResumeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resume");
        group.MapPost("/parse-text", ParseText).RequireQuota();                        // synchronous, for quick tests
        group.MapPost("/parse-file", ParseFile).DisableAntiforgery().RequireQuota();   // asynchronous: returns a job id
        return app;
    }

    private static async Task<IResult> ParseText(
        ParseTextRequest req, HttpContext ctx, ResumeParserService parser, IUsageRecorder usage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return Results.BadRequest(new { error = "Text is required." });

        var sw = Stopwatch.StartNew();
        var outcome = await parser.ParseAsync(req.Text, ct);
        await usage.RecordAsync(new UsageRecord(ctx.GetCaller()?.KeyId, "resume", "parse-text", outcome.Usage, null), ct);
        return Results.Ok(new { durationMs = sw.ElapsedMilliseconds, data = outcome.Data });
    }

    private static async Task<IResult> ParseFile(
        IFormFile file, HttpContext ctx, ResumeUploadService uploads, CancellationToken ct)
    {
        var result = await uploads.SubmitAsync(file, ctx.GetCaller()?.KeyId, ct);
        return result.Success
            ? Results.Accepted($"/api/jobs/{result.JobId}", new { jobId = result.JobId, status = "Queued" })
            : Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
    }
}
