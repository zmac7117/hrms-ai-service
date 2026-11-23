using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HrmsAi.Chat;

// Attendance reply written by code from the HRMS data (never by the AI), so the numbers are exact.
// The HRMS sends the whole current month. This class picks today / this week / this month from the question.
public static class AttendanceReply
{
    private const int MaxRows = 15;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] StatusOrder = { "Present", "Half Day", "Leave", "Absent", "Holiday", "Weekly Off" };

    private enum Period { Today, Week, Month }

    private record Day(DateOnly Date, string Status, string In, string Out, double? Hours);
    private record Parts(string Intro, string Title, string[] Columns, List<string[]> Rows);

    private static readonly Regex TodayWords = new(@"\b(today|right now)\b|\b(punch|check)(ed)?[- ]?(in|out)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WeekWords = new(@"\bweek\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Period PeriodOf(string question) =>
        TodayWords.IsMatch(question) ? Period.Today
        : WeekWords.IsMatch(question) ? Period.Week
        : Period.Month;

    // Plain text for the chat history. Null when the JSON is bad.
    public static string? Format(string json, string name, string question)
    {
        var p = Compose(json, name, question);
        if (p is null) return null;
        if (p.Rows.Count == 0) return p.Intro;
        var sb = new StringBuilder(p.Intro);
        foreach (var row in p.Rows) sb.Append("\n- ").Append(string.Join(" | ", row));
        return sb.ToString();
    }

    // Intro sentence + table card. Null when there is nothing to show (then the caller sends the plain text).
    public static CardReply? Build(string json, string name, string question)
    {
        var p = Compose(json, name, question);
        if (p is null || p.Rows.Count == 0) return null;
        return new CardReply(p.Intro, new { type = "table", title = p.Title, columns = p.Columns, rows = p.Rows });
    }

    private static Parts? Compose(string json, string name, string question)
    {
        var data = Load(json);
        if (data is null) return null;
        var (today, days) = data.Value;

        name = ChatText.Clean(name ?? "",60);
        var lead = name.Length == 0 ? "Here is" : name + ", here is";
        var empty = new List<string[]>();
        var period = PeriodOf(question);
        Parts parts;

        if (period == Period.Month)
        {
            var upto = days.Where(d => d.Date <= today).ToList();
            if (upto.Count == 0)
                return new Parts($"I don't see any attendance records for {Fmt(today, "MMMM yyyy")} yet.", "", Array.Empty<string>(), empty);

            var rows = upto.GroupBy(d => d.Status, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key)
                .Select(g => new[] { g.Key, g.Count().ToString(Inv) })
                .ToList();
            var hours = upto.Sum(d => d.Hours ?? 0);
            var intro = $"{lead} your attendance for {Fmt(today, "MMMM yyyy")}, up to {Fmt(today, "dd MMM")}.";
            if (hours > 0) intro += $" Total hours worked: {hours.ToString("0.##", Inv)}.";
            parts = new Parts(intro, "Attendance - this month", new[] { "Status", "Days" }, rows);
        }
        else
        {
            var from = period == Period.Today ? today : today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // Monday
            var picked = days.Where(d => d.Date >= from && d.Date <= today).ToList();
            var label = period == Period.Today ? $"today ({Fmt(today, "ddd dd MMM")})" : "this week";
            if (picked.Count == 0)
                return new Parts($"I don't see an attendance record for {label} yet.", "", Array.Empty<string>(), empty);

            var rows = picked.Select(d => new[]
                { Fmt(d.Date, "ddd dd MMM"), d.Status, Dash(d.In), Dash(d.Out) }).ToList();
            var intro = period == Period.Today
                ? $"{lead} your attendance for {label}."
                : $"{lead} your attendance for this week ({Fmt(from, "dd MMM")} to {Fmt(today, "dd MMM")}).";
            parts = new Parts(intro, period == Period.Today ? "Attendance - today" : "Attendance - this week",
                new[] { "Date", "Status", "In", "Out" }, rows);
        }

        // Keep the card small (the widget is narrow).
        if (parts.Rows.Count > MaxRows)
            parts = parts with { Intro = parts.Intro + $" Showing {MaxRows} of {parts.Rows.Count}.", Rows = parts.Rows.Take(MaxRows).ToList() };
        return parts;
    }

    private static (DateOnly Today, List<Day> Days)? Load(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var daysEl = Prop(root, "days");
            if (daysEl is null || daysEl.Value.ValueKind != JsonValueKind.Array) return null;

            var today = DateOnly.FromDateTime(DateTime.Now);
            if (DateOnly.TryParseExact(Str(root, "today"), "yyyy-MM-dd", Inv, DateTimeStyles.None, out var t)) today = t;

            var days = new List<Day>();
            foreach (var d in daysEl.Value.EnumerateArray())
            {
                if (!DateOnly.TryParseExact(Str(d, "date"), "yyyy-MM-dd", Inv, DateTimeStyles.None, out var date)) continue;
                var status = Str(d, "status");
                if (status.Length == 0) continue;
                days.Add(new Day(date, status, Str(d, "checkIn"), Str(d, "checkOut"), Num(d, "hours")));
            }
            return (today, days.OrderBy(x => x.Date).ToList());
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // Case-insensitive property lookup.
    private static JsonElement? Prop(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string Str(JsonElement obj, string name)
    {
        var v = Prop(obj, name);
        if (v is null) return "";
        return v.Value.ValueKind switch
        {
            JsonValueKind.String => ChatText.Clean(v.Value.GetString() ?? "", 60),
            JsonValueKind.Number => v.Value.GetDouble().ToString("0.##", Inv),
            _ => ""
        };
    }

    private static double? Num(JsonElement obj, string name)
    {
        var v = Prop(obj, name);
        return v is { ValueKind: JsonValueKind.Number } ? v.Value.GetDouble() : null;
    }

    private static string Fmt(DateOnly d, string format) => d.ToString(format, Inv);
    private static string Dash(string s) => s.Length == 0 ? "-" : s;

    private static int Rank(string status)
    {
        var i = Array.FindIndex(StatusOrder, o => o.Equals(status, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? 99 : i;
    }
}