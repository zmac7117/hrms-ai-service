using HrmsAi.Abstractions;

namespace HrmsAi.Platform.Jobs;

public class AiGate(IConfiguration config) : IAiGate
{
    private readonly SemaphoreSlim _slots = new(Math.Clamp(config.GetValue<int?>("Queue:Concurrency") ?? 1, 1, 8));

    public async Task<IDisposable?> AcquireAsync(TimeSpan maxWait, CancellationToken ct)
    {
        if (!await _slots.WaitAsync(maxWait, ct)) return null;
        return new Releaser(_slots);
    }

    private sealed class Releaser(SemaphoreSlim slots) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}
