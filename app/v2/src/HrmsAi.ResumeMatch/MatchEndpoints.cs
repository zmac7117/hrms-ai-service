using HrmsAi.Abstractions;
using Microsoft.Extensions.Options;

namespace HrmsAi.ResumeMatch;

// Same addresses and answers as the SharpAPI service the HRMS uses today.
// In the HRMS, set SharpAPI:BaseUrl to  https://<this service>/api/match
public static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/match");
        group.MapPost("/api/v1/hr/resume_job_match_score", Submit).DisableAntiforgery().RequireQuota();
        group.MapGet("/api/v1/hr/resume_job_match_score/job/status/{id:guid}", Status);
        return app;
    }

    private static async Task<IResult> Submit(HttpContext ctx, MatchSubmitService svc, IOptions<MatchSettings> s, CancellationToken ct)
    {
        var r = await svc.SubmitAsync(ctx, ct);
        if (!r.Ok) return Results.Json(new { error = r.Error }, statusCode: r.StatusCode);

        var url = $"{PublicBase(ctx, s.Value)}/api/match/api/v1/hr/resume_job_match_score/job/status/{r.JobId}";
        return Results.Json(new { job_id = r.JobId, status_url = url }, statusCode: 202);
    }

    private static async Task<IResult> Status(Guid id, HttpContext ctx, IJobStore jobs)
    {
        var job = await jobs.GetAsync(id);
        var caller = ctx.GetCaller();
        if (job is null || job.JobType != "match" || (caller?.KeyId is not null && job.ApiKeyId != caller.KeyId))
            return Results.Json(new { error = "Unknown job." }, statusCode: 404);

        var status = job.Status switch { "Done" => "success", "Failed" => "failed", "Running" => "processing", _ => "pending" };
        return Results.Json(new
        {
            data = new
            {
                type = "api_job_result",
                id = job.Id,
                attributes = new
                {
                    status,
                    type = "hr_resume_job_match_score",
                    result = status == "success" ? job.ResultJson : null,   // a JSON string, as the HRMS expects
                    error = status == "failed" ? job.Error : null
                }
            }
        });
    }

    // The address the HRMS can use to poll, also when we sit behind ngrok or another proxy
    private static string PublicBase(HttpContext ctx, MatchSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.PublicBaseUrl)) return s.PublicBaseUrl.TrimEnd('/');
        var proto = ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault()?.Split(',')[0].Trim();
        var host = ctx.Request.Headers["X-Forwarded-Host"].FirstOrDefault()?.Split(',')[0].Trim();
        return $"{(string.IsNullOrEmpty(proto) ? ctx.Request.Scheme : proto)}://{(string.IsNullOrEmpty(host) ? ctx.Request.Host.Value : host)}{ctx.Request.PathBase}";
    }
}
