namespace HrmsAi.Chat;

public record StartChatRequest(string ExternalUserId, string? UserName, string? Language, Guid? ConversationId);
public record StartChatResult(Guid ConversationId, string Greeting, bool IsNew);

public record SendMessageRequest(Guid ConversationId, string ExternalUserId, string Text);
public record SendResult(bool Accepted, Guid? MessageId, string? ErrorCode);

public record AnswerRequest(Guid ConversationId, Guid MessageId, string ConnectionId, Guid? KeyId, string ExternalUserId, string UserName, string Text);

public class ConversationRow
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = "";
    public string Language { get; set; } = "en";
}

public class HistoryItem
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public static class ChatText
{
    // Removes line breaks and control characters, so a name can never act as an instruction.
    public static string Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var chars = value.Where(c => !char.IsControl(c)).ToArray();
        var s = new string(chars).Trim();
        return s.Length > max ? s[..max] : s;
    }

    public static string Greeting(string name) =>
        name.Length == 0 ? "Hi, I'm Sia. How can I help you?" : $"Hi {name}, I'm Sia. How can I help you?";

    public static string WelcomeBack(string name) =>
        name.Length == 0 ? "Welcome back, how can I help you?" : $"Welcome back {name}, how can I help you?";

    public static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}