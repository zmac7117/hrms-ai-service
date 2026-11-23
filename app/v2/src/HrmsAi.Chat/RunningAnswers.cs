using System.Collections.Concurrent;

namespace HrmsAi.Chat;

// Allows one running answer per conversation, and stops answers when their connection drops.
public class RunningAnswers
{
    private readonly ConcurrentDictionary<Guid, (string ConnectionId, CancellationTokenSource Cts)> _running = new();

    // Returns null when this conversation already has a running answer.
    public CancellationTokenSource? TryStart(Guid conversationId, string connectionId)
    {
        var cts = new CancellationTokenSource();
        if (_running.TryAdd(conversationId, (connectionId, cts))) return cts;
        cts.Dispose();
        return null;
    }

    public void Finish(Guid conversationId)
    {
        if (_running.TryRemove(conversationId, out var entry)) entry.Cts.Dispose();
    }

    public void CancelConnection(string connectionId)
    {
        foreach (var item in _running.Where(r => r.Value.ConnectionId == connectionId))
        {
            try { item.Value.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }
}