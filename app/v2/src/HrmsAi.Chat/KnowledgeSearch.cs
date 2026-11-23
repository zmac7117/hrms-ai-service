using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HrmsAi.Chat;

public record KnowledgeHit(string Title, string Content, double Score);
public record KnowledgeResult(bool HasDocuments, IReadOnlyList<KnowledgeHit> Hits);

// Finds the best pieces for a question by word matching (BM25). No AI, no extra model.
// The pieces of each key are kept in memory; an upload or delete clears them.
public class KnowledgeSearch(KnowledgeStore store, IMemoryCache cache, IOptions<ChatSettings> options, ILogger<KnowledgeSearch> log)
{
    private class Entry
    {
        public string Title = "";
        public string Content = "";
        public Dictionary<string, int> Tf = new();
        public int Length;
    }

    private class Index
    {
        public List<Entry> Entries = new();
        public Dictionary<string, int> Df = new();
        public double AverageLength;
    }

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","is","are","am","was","were","be","i","my","me","we","you","your","our","it","its","this","that",
        "these","those","how","what","when","where","who","which","why","to","do","does","did","can","could","will","would",
        "should","may","for","of","in","on","at","by","and","or","with","from","as","if","so","about","into","there","have",
        "has","had","get","got","please","tell","give","show","want","need","know","ok","okay"
    };

    public static List<string> Tokenize(string text)
    {
        var result = new List<string>();
        foreach (Match m in Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+"))
        {
            var w = m.Value;
            if (w.Length < 2 || Stop.Contains(w)) continue;
            result.Add(Stem(w));
        }
        return result;
    }

    // Very light stemming so "leaves" matches "leave" and "applying" matches "apply".
    private static string Stem(string w)
    {
        if (w.Length > 5 && w.EndsWith("ing")) return w[..^3];
        if (w.Length > 4 && w.EndsWith("ied")) return w[..^3] + "y";
        if (w.Length > 4 && w.EndsWith("ed")) return w[..^2];
        if (w.Length > 3 && w.EndsWith("s") && !w.EndsWith("ss")) return w[..^1];
        return w;
    }

    private static string CacheKey(Guid? keyId) => $"kb:{keyId}";

    public void Invalidate(Guid? keyId) => cache.Remove(CacheKey(keyId));

    private async Task<Index> GetIndexAsync(Guid? keyId, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(keyId), out Index? cached) && cached is not null) return cached;

        var rows = await store.LoadChunksAsync(keyId, ct);
        var index = new Index();
        foreach (var row in rows)
        {
            var tokens = Tokenize(row.Title + " " + row.Content);
            var entry = new Entry { Title = row.Title, Content = row.Content, Length = tokens.Count };
            foreach (var t in tokens) entry.Tf[t] = entry.Tf.GetValueOrDefault(t) + 1;
            foreach (var t in entry.Tf.Keys) index.Df[t] = index.Df.GetValueOrDefault(t) + 1;
            index.Entries.Add(entry);
        }
        index.AverageLength = index.Entries.Count == 0 ? 1 : Math.Max(1, index.Entries.Average(e => e.Length));
        cache.Set(CacheKey(keyId), index, TimeSpan.FromHours(6));
        return index;
    }

    public async Task<KnowledgeResult> SearchAsync(Guid? keyId, string query, CancellationToken ct)
    {
        var cfg = options.Value;
        var index = await GetIndexAsync(keyId, ct);
        if (index.Entries.Count == 0) return new KnowledgeResult(false, Array.Empty<KnowledgeHit>());

        var terms = Tokenize(query).Distinct().ToList();
        if (terms.Count == 0) return new KnowledgeResult(true, Array.Empty<KnowledgeHit>());

        const double k1 = 1.5, b = 0.75;
        var n = index.Entries.Count;
        var scored = new List<KnowledgeHit>();
        foreach (var e in index.Entries)
        {
            double score = 0;
            foreach (var t in terms)
            {
                if (!e.Tf.TryGetValue(t, out var tf)) continue;
                var df = index.Df.GetValueOrDefault(t);
                var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
                score += idf * (tf * (k1 + 1)) / (tf + k1 * (1 - b + b * e.Length / index.AverageLength));
            }
            if (score > 0) scored.Add(new KnowledgeHit(e.Title, e.Content, score));
        }

        var hits = scored.OrderByDescending(h => h.Score).Take(cfg.TopChunks).Where(h => h.Score >= cfg.MinScore).ToList();
        log.LogInformation("Knowledge search terms=[{Terms}] best={Best:F2} used={Used}",
            string.Join(",", terms), scored.Count == 0 ? 0 : scored.Max(h => h.Score), hits.Count);
        return new KnowledgeResult(true, hits);
    }
}