using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace HrmsAi.Compat;

// A short, readable description of what was asked, for the dashboard
public static class PromptSummary
{
    private static readonly string[] Labels = { "Job Title", "Career Goal Description", "Position", "Current Position" };

    public static string Describe(IReadOnlyList<ChatMessage> messages)
    {
        var text = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        foreach (var label in Labels)
        {
            var m = Regex.Match(text, $@"^\s*{Regex.Escape(label)}\s*:\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Value.Trim().Length > 0) return Cap($"{label}: {m.Groups[1].Value.Trim()}", 300);
        }
        return Cap(Regex.Replace(text, @"\s+", " ").Trim(), 120);
    }

    private static string Cap(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
