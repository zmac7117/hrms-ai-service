using HrmsAi.Access.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace HrmsAi.Access.Services;

// Keeps validated keys in memory for 30 seconds so most requests skip the database.
// Any admin change clears everything, so deactivations apply at once in this process.
public class KeyCache(IMemoryCache cache)
{
    private CancellationTokenSource _cts = new();

    public ApiKeyRecord? Get(string hashHex) => cache.TryGetValue(hashHex, out ApiKeyRecord? rec) ? rec : null;

    public void Set(string hashHex, ApiKeyRecord rec)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(30))
            .AddExpirationToken(new CancellationChangeToken(_cts.Token));
        cache.Set(hashHex, rec, options);
    }

    public void Clear()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}
