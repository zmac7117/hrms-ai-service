using System.Text.RegularExpressions;
using HrmsAi.Abstractions;
using Microsoft.Extensions.AI;

namespace HrmsAi.Chat;

// Decides if a message is about HR / the HRMS. Cheap checks first, AI only for the unclear ones.
// When the AI answer is unclear we let the message through (the system prompt is a second safety net).
public static class ScopeGuard
{
    public record Verdict(bool InScope, AiUsage Usage, string Reason);

    private static readonly HashSet<string> Greetings = new(StringComparer.OrdinalIgnoreCase)
    {
        "hi", "hii", "hello", "hey", "hi there", "hello there", "thanks", "thank you", "thankyou", "thx",
        "ok", "okay", "bye", "goodbye", "good morning", "good afternoon", "good evening", "yes", "no",
        "who are you", "what can you do", "help"
    };

    private static readonly Regex HrWords = new(
        @"\b(leave|leaves|attendance|absent|present|salary|salaries|payslip|payslips|payroll|pay|holiday|holidays|" +
        @"policy|policies|hrms|hr|resign|resignation|department|designation|employee|employees|manager|promotion|" +
        @"appraisal|performance|goal|goals|recruit|recruitment|interview|job|overtime|shift|timesheet|benefit|benefits|" +
        @"insurance|bonus|probation|notice|punch|login|password|profile|expense|expenses|reimbursement|onboarding|" +
        @"document|documents|office|company|team|apply|request|approve|approval|work|working|joining|dob|address)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<Verdict> CheckAsync(
        IChatClient chat, IReadOnlyList<HistoryItem> history, string text, string refusalText, CancellationToken ct, string? model = null)
    {
        var trimmed = text.Trim().Trim('.', '!', '?', ',', ' ');
        if (Greetings.Contains(trimmed)) return new Verdict(true, AiUsage.None, "greeting");
        if (HrWords.IsMatch(text)) return new Verdict(true, AiUsage.None, "keyword");

        var messages = ChatPrompt.BuildScopeCheck(history, text, refusalText);
        var options = new ChatOptions { Temperature = 0, MaxOutputTokens = 4, ModelId = string.IsNullOrWhiteSpace(model) ? null : model };

        var response = await chat.GetResponseAsync(messages, options, ct);
        var word = (response.Text ?? "").Trim().ToUpperInvariant();

        var chars = messages.Sum(m => m.Text?.Length ?? 0);
        var usage = AiUsage.From(response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, chars);
        var refused = word.StartsWith("NO");
        return new Verdict(!refused, usage, "ai:" + word);
    }
}