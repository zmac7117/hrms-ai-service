using System.Text;
using System.Text.Json;

namespace HrmsAi.Chat;

public record CardReply(string Intro, object Card);

// Builds the leave balance reply from the HRMS data. Plain code, so numbers are never invented.
public static class LeaveBalanceReply
{
    private record Item(string Type, string Total, string Used, string Remaining);
    private record Picked(string Who, bool Matched, List<Item> Shown);

    // Full text version (stored in the history, used when a client cannot show cards).
    public static string? Format(string json, string name, string question)
    {
        var p = Pick(json, name, question);
        if (p is null) return null;
        if (p.Shown.Count == 0) return $"I couldn't find any leave balance for you{p.Who}. Please check with HR.";

        var sb = new StringBuilder(Intro(p));
        foreach (var i in p.Shown)
        {
            sb.Append($"\n- {i.Type}: {i.Remaining} remaining");
            var extra = new List<string>();
            if (i.Total.Length > 0) extra.Add($"{i.Total} total");
            if (i.Used.Length > 0) extra.Add($"{i.Used} used");
            if (extra.Count > 0) sb.Append($" ({string.Join(", ", extra)})");
        }
        return sb.ToString();
    }

    // Intro sentence + a table card. Null when there is nothing to put in a table.
    public static CardReply? Build(string json, string name, string question)
    {
        var p = Pick(json, name, question);
        if (p is null || p.Shown.Count == 0) return null;

        var rows = p.Shown.Select(i => new[] { i.Type, i.Total, i.Used, i.Remaining }).ToArray();
        var card = new
        {
            type = "table",
            title = "Leave balance",
            columns = new[] { "Leave type", "Total", "Used", "Remaining" },
            rows
        };
        return new CardReply(Intro(p), card);
    }

    private static string Intro(Picked p) =>
        p.Matched ? $"Here is your balance{p.Who}:" : $"Here is your leave balance{p.Who}:";

    private static Picked? Pick(string json, string name, string question)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = doc.RootElement;
            if (list.ValueKind == JsonValueKind.Object) list = Get(list, "balances") ?? default;
            if (list.ValueKind != JsonValueKind.Array) return null;

            var items = new List<Item>();
            foreach (var e in list.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var type = Text(Get(e, "leaveType"));
                if (type.Length == 0) continue;
                items.Add(new Item(type, Text(Get(e, "total")), Text(Get(e, "used")), Text(Get(e, "remaining"))));
            }

            var who = name.Length == 0 ? "" : $", {name}";

            // "sick leave balance" shows only the sick leave row.
            var q = question.ToLowerInvariant();
            var matched = items.Where(i =>
            {
                var key = i.Type.ToLowerInvariant().Replace("leave", "").Trim();
                return key.Length >= 3 && q.Contains(key);
            }).ToList();
            return new Picked(who, matched.Count > 0, matched.Count > 0 ? matched : items);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Get(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string Text(JsonElement? e)
    {
        if (e is null) return "";
        var v = e.Value;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal().ToString("0.##"),
            JsonValueKind.String => ChatText.Clean(v.GetString(), 60),
            _ => ""
        };
    }
}