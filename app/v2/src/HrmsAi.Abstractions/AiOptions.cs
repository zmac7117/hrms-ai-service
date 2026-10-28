namespace HrmsAi.Abstractions;

public class AiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen2.5:3b";
    public int TimeoutSeconds { get; set; } = 300;
}
