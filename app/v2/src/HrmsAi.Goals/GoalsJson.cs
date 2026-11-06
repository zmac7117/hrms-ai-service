using System.Text.Json;

namespace HrmsAi.Goals;

public static class GoalsJson
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // Removes code fences and any text around the JSON
    public static string Extract(string text)
    {
        var t = (text ?? "").Replace("```json", "").Replace("```", "").Trim();
        var start = t.IndexOfAny(new[] { '[', '{' });
        if (start < 0) return t;

        var close = t[start] == '[' ? ']' : '}';
        var end = t.LastIndexOf(close);
        if (end <= start) return t;

        var candidate = t.Substring(start, end - start + 1);
        try { using var _ = JsonDocument.Parse(candidate); return candidate; }
        catch (JsonException) { return t; }
    }

    // For the Groq-style route
    public static string Normalize(string text, GoalsSettings s)
    {
        var json = Extract(text);
        if (!s.WrapArrayAsObject) return json;
        return json.TrimStart().StartsWith("[") ? "{\"" + s.ObjectKey + "\":" + json + "}" : json;
    }

    // For the native route: accepts an array, or an object that contains one
    public static bool TryParse(string json, out List<GoalItem> goals)
    {
        goals = new List<GoalItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var array = doc.RootElement;
            if (array.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Array) { array = p.Value; break; }
            }
            if (array.ValueKind != JsonValueKind.Array) return false;

            var list = JsonSerializer.Deserialize<List<GoalItem>>(array.GetRawText(), Web) ?? new List<GoalItem>();
            goals = list.Where(x => !string.IsNullOrWhiteSpace(x.Goal)).ToList();
            return goals.Count > 0;
        }
        catch (JsonException) { return false; }
    }
}
