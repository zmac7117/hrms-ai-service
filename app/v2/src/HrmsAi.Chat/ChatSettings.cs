namespace HrmsAi.Chat;

// Config section "Chat". Every value has a default, so appsettings needs no change.
public class ChatSettings
{
    // Ollama model for chat (answers and scope check). Empty = the default Ai:ChatModel.
    public string? Model { get; set; }
    public string? ScopeModel { get; set; }   // optional smaller model for the scope check only

    public double Temperature { get; set; } = 0.5;
    public int MaxOutputTokens { get; set; } = 300;   // about 20 seconds of writing on this CPU
    public int HistoryMessages { get; set; } = 6;
    public int MaxMessageChars { get; set; } = 1000;
    public int MaxWaitSeconds { get; set; } = 30;

    // Scope control: questions that are not about HR / the HRMS get the fixed reply below.
    public bool ScopeCheck { get; set; } = true;
    public string OutOfScopeReply { get; set; } =
        "I can only help with HR and HRMS questions, such as leave, attendance, payslips and company policies. Please ask me something about those.";

    // Data from the HRMS (RunAction)
    public int ActionTimeoutSeconds { get; set; } = 10;
    public int ProfileCacheMinutes { get; set; } = 30;
    public int MaxProfileChars { get; set; } = 4000;
    public string HrSystemDownReply { get; set; } =
        "I can't reach the HR system right now, please try again in a moment.";

    // Knowledge (uploaded documents)
    public int TopChunks { get; set; } = 4;               // pieces sent to the AI
    public double MinScore { get; set; } = 0.1;           // lower = more matches, higher = stricter
    public int MaxChunkChars { get; set; } = 700;
    public int MaxDocumentChars { get; set; } = 300000;
    public int MaxChunksPerDocument { get; set; } = 300;
    public bool StrictKnowledge { get; set; } = true;     // HR question + no match = fixed reply, no guessing
    public string NoAnswerReply { get; set; } =
        "I don't have information about that. Please ask HR for help.";
}