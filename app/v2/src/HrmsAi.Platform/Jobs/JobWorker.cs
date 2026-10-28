using System.Diagnostics;
using HrmsAi.Abstractions;
using Microsoft.Extensions.Options;

namespace HrmsAi.Platform.Jobs;

// Hands each job to the module that owns its type. Runs Queue:Concurrency jobs at the same time.
public class JobWorker(
    IJobStore store,
    IEnumerable<IJobHandler> handlerList,
    IUsageRecorder usage,
    IOptions<QueueSettings> queue,
    ILogger<JobWorker> log) : BackgroundService
{
    private readonly Dictionary<string, IJobHandler> _handlers = handlerList.ToDictionary(h => h.JobType);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var types = _handlers.Keys.ToArray();
        if (types.Length == 0) { log.LogWarning("No job handlers are installed. The worker is idle."); return; }

        try { await store.ResetRunningAsync(types); }
        catch (Exception ex) { log.LogError(ex, "Could not reset running jobs. Is SQL Server reachable?"); }

        var workers = Math.Clamp(queue.Value.Concurrency, 1, 8);
        log.LogInformation("Job worker started with {Workers} parallel worker(s) for: {Types}", workers, string.Join(", ", types));

        await Task.WhenAll(Enumerable.Range(1, workers).Select(n => RunLoopAsync(types, stop)));
    }

    private async Task RunLoopAsync(string[] types, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            AiJob? job = null;
            try { job = await store.ClaimNextAsync(types); }
            catch (Exception ex) { log.LogError(ex, "Database error while claiming a job"); }

            if (job is null)
            {
                try { await Task.Delay(2000, stop); } catch (OperationCanceledException) { break; }
                continue;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var handler = _handlers[job.JobType];
                var outcome = await handler.HandleAsync(job, stop);
                await store.CompleteAsync(job.Id, outcome.ResultJson, (int)sw.ElapsedMilliseconds);
                log.LogInformation("Job {Id} ({Type}) done in {Ms} ms, {Prompt} prompt + {Output} output tokens",
                    job.Id, job.JobType, sw.ElapsedMilliseconds, outcome.Usage.PromptTokens, outcome.Usage.CompletionTokens);

                try
                {
                    await usage.RecordAsync(new UsageRecord(job.ApiKeyId, handler.Module, job.JobType, outcome.Usage, job.Id), stop);
                }
                catch (Exception ex) { log.LogError(ex, "Could not record usage for job {Id}", job.Id); }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Job {Id} failed", job.Id);
                await store.FailAsync(job.Id, ex.Message);
            }
        }
    }
}
