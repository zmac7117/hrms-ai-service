using System.Text.RegularExpressions;

namespace HrmsAi.Chat;

// Things that need no AI: greetings, identity questions and "is this about the user's own details".
public static class ChatShortcuts
{
    private static readonly Regex ProfileWords = new(
        @"\b(department|departments|sub[- ]?department|designation|job title|manager|reporting to|reports to|" +
        @"joining date|date of joining|joined|employee (id|code|number)|my (details|profile|info|information|email|" +
        @"phone|mobile|branch|location|grade|role|position|team)|which (team|branch|grade)|who is my)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool WantsProfile(string text) => ProfileWords.IsMatch(text);

    // "how many leaves do I have", "sick leave balance", "leaves left" - but not "how do I apply for leave".
    private static readonly Regex LeaveBalanceWords = new(
        @"\bhow many (\w+ )?leaves?\b|\bleaves? (balance|left|remaining|available|count)\b|" +
        @"\b(balance|remaining|available|left) (of )?(\w+ )?leaves?\b|\bleaves? (do )?i (have|got)\b|\bmy leaves?\b.*\b(left|balance|remaining)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool WantsLeaveBalance(string text) => LeaveBalanceWords.IsMatch(text);

    // "my attendance this month", "did I punch in today" - but not "how do I regularize attendance" (that is a how-to).
    private static readonly Regex AttendanceWords = new(
        @"\bmy attendance\b|\battendance (today|this (week|month)|summary|status|record|details)\b|" +
        @"\b(am i|was i|did i) (present|absent|late)\b|\b(did i|have i) (punch|check)(ed)?[- ]?in\b|" +
        @"\bmy (check|punch)[- ]?(in|out) (time|today)\b|\bmy (working|worked) hours\b|" +
        @"\bhow many days\b.*\b(present|absent|worked)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AttendanceHowTo = new(
        @"\b(how (do|can|to|should)|regulari[sz]e|regulari[sz]ation|policy|rules?|apply|request|correct|missed|forgot)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool WantsAttendance(string text) => AttendanceWords.IsMatch(text) && !AttendanceHowTo.IsMatch(text);

    private static readonly Regex QuestionStart = new(
        @"^\s*(how|what|when|where|who|whom|which|why|can|could|do|does|did|is|are|will|should|may|tell|explain|give|show|list)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "how do I apply for leave" is a question; "i work in engineering" is not.
    public static bool IsQuestion(string text) => text.Contains('?') || QuestionStart.IsMatch(text);

    // Returns null when the message needs the AI.
    public static string? Reply(string text, string name)
    {
        var t = text.Trim().Trim('.', '!', '?', ',', ' ').ToLowerInvariant();
        var hello = name.Length == 0 ? "Hello, I'm Sia. How can I help you?" : $"Hello {name}, I'm Sia. How can I help you?";
        return t switch
        {
            "hi" or "hii" or "hello" or "hey" or "hi there" or "hello there"
                or "good morning" or "good afternoon" or "good evening" or "hi sia" or "hello sia" or "hey sia" => hello,
            "thanks" or "thank you" or "thankyou" or "thx" or "thanks sia" or "thank you sia" =>
                "You're welcome! Is there anything else I can help with?",
            "bye" or "goodbye" => "Goodbye, have a great day!",
            "who are you" or "who r u" or "what is your name" or "what's your name" or "your name" or "what are you"
                or "tell me about yourself" or "introduce yourself" =>
                "I'm Sia, the Sapience Intelligent Assistant. I can help you with leave, attendance, payslips, HR policies and your own employee details.",
            "who made you" or "who created you" or "who built you" or "who developed you" =>
                "I'm Sia, the Sapience Intelligent Assistant, built into your HRMS.",
            "what can you do" or "help" =>
                "I'm Sia. Ask me about leave, attendance, payslips, policies, how to use the HRMS, or your own employee details.",
            _ => null
        };
    }
}