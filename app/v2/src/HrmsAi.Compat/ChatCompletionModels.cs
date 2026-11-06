using System.Text.Json.Serialization;

namespace HrmsAi.Compat;

// The OpenAI / Groq "chat completions" request and response shapes
public class ChatCompletionRequest
{
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("messages")] public List<ChatCompletionMessage> Messages { get; set; } = new();
    [JsonPropertyName("temperature")] public double? Temperature { get; set; }
    [JsonPropertyName("max_tokens")] public int? MaxTokens { get; set; }
    [JsonPropertyName("max_completion_tokens")] public int? MaxCompletionTokens { get; set; }
    [JsonPropertyName("response_format")] public ChatResponseFormatDto? ResponseFormat { get; set; }
    [JsonPropertyName("stream")] public bool? Stream { get; set; }
}

public class ChatCompletionMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string? Content { get; set; }
}

public class ChatResponseFormatDto
{
    [JsonPropertyName("type")] public string? Type { get; set; }
}

public class ChatCompletionResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("object")] public string ObjectType { get; set; } = "chat.completion";
    [JsonPropertyName("created")] public long Created { get; set; }
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("choices")] public List<ChatCompletionChoice> Choices { get; set; } = new();
    [JsonPropertyName("usage")] public ChatCompletionUsage Usage { get; set; } = new();
}

public class ChatCompletionChoice
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("message")] public ChatCompletionMessage Message { get; set; } = new();
    [JsonPropertyName("finish_reason")] public string FinishReason { get; set; } = "stop";
}

public class ChatCompletionUsage
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
}
