namespace HrmsAi.Abstractions;

// A limited number of AI slots shared by background jobs and direct calls,
// so the CPU is never asked to do more at once than it can.
public interface IAiGate
{
    // Returns null when no slot became free within maxWait.
    Task<IDisposable?> AcquireAsync(TimeSpan maxWait, CancellationToken ct);
}
