using HrmsAi.Abstractions;
using HrmsAi.Platform.Jobs;
using HrmsAi.Platform.Mapping;
using Microsoft.Extensions.Options;

namespace HrmsAi.Platform.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs");
        group.MapGet("/{id:guid}", GetJob);
        group.MapGet("/", ListJobs);
        return app;
    }

    // A caller with its own key only sees jobs created with that key
    private static async Task<IResult> GetJob(Guid id, HttpContext ctx, IJobStore store, IOptions<QueueSettings> queue)
    {
        var job = await store.GetAsync(id);
        var caller = ctx.GetCaller();
        if (job is null || (caller?.KeyId is not null && job.ApiKeyId != caller.KeyId))
            return Results.NotFound();

        var dto = job.ToDto();
        var workers = Math.Clamp(queue.Value.Concurrency, 1, 8);

        if (job.Status == "Queued")
        {
            var pos = await store.GetQueuePositionAsync(id);
            if (pos is not null)
            {
                var avg = await store.AverageDurationSecondsAsync(job.JobType) ?? 30;
                var eta = (int)Math.Ceiling(((pos.Value - 1.0) / workers + 1) * avg);
                dto = dto with { QueuePosition = pos, EstimatedSecondsRemaining = eta };
            }
        }
        else if (job.Status == "Running" && job.StartedAt is not null)
        {
            var avg = await store.AverageDurationSecondsAsync(job.JobType) ?? 30;
            var elapsed = (DateTime.UtcNow - job.StartedAt.Value).TotalSeconds;
            dto = dto with { EstimatedSecondsRemaining = (int)Math.Max(3, Math.Ceiling(avg - elapsed)) };
        }
        return Results.Ok(dto);
    }

    private static async Task<IResult> ListJobs(HttpContext ctx, IJobStore store, int? take)
    {
        var jobs = await store.ListAsync(Math.Clamp(take ?? 20, 1, 100), ctx.GetCaller()?.KeyId);
        return Results.Ok(jobs.Select(j => j.ToDto()));
    }
}
