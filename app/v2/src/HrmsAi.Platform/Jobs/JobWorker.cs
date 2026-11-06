using System.Diagnostics;
using HrmsAi.Abstractions;

namespace HrmsAi.Platform.Jobs;

// Hands each job to the module that owns its type. Every job first takes an AI slot.
public class JobWorker(
    IJobStore store,
    IEnumerable<IJobHandler> handlerList,
    IUsageRecorder usage,
    IAiGate gate,
    IConfiguration config,
    ILogger<JobWorker> log) : BackgroundService
{
    private readonly Dictionary<string, IJobHandler> _handlers = handlerList.ToDictionary(h => h.JobType);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var types = _handlers.Keys.ToArray();
        if (types.Length == 0) { log.LogWarning("No job handlers are installed. The worker is idle."); return; }

        try { await store.ResetRunningAsync(types); }
        catch (Exception ex) { log.LogError(ex, "Could not reset running jobs. Is SQL Server reachable?"); }

        var workers = Math.Clamp(config.GetValue<int?>("Queue:Concurrency") ?? 1, 1, 8);
        log.LogInformation("Job worker started with {Workers} parallel worker(s) for: {Types}", workers, string.Join(", ", types));

        await Task.WhenAll(Enumerable.Range(1, workers).Select(_ => RunLoopAsync(types, stop)));
    }

    private async Task RunLoopAsync(string[] types, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            IDisposable? slot;
            try { slot = await gate.AcquireAsync(Timeout.InfiniteTimeSpan, stop); }
            catch (OperationCanceledException) { break; }

            try
            {
                AiJob? job = null;
                try { job = await store.ClaimNextAsync(types); }
                catch (Exception ex) { log.LogError(ex, "Database error while claiming a job"); }

                if (job is null)
                {
                    slot?.Dispose();
                    slot = null;
                    try { await Task.Delay(2000, stop); } catch (OperationCanceledException) { break; }
                    continue;
                }
                await RunJobAsync(job, stop);
            }
            finally { slot?.Dispose(); }
        }
    }

    private async Task RunJobAsync(AiJob job, CancellationToken stop)
    {
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
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.LogError(ex, "Job {Id} failed", job.Id);
            await store.FailAsync(job.Id, ex.Message);
        }
    }
}
