namespace HrmsAi.Abstractions;

public class AiJob
{
    public Guid Id { get; set; }
    public string JobType { get; set; } = "";
    public string Status { get; set; } = "";
    public string? InputFileName { get; set; }
    public string? InputText { get; set; }
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public Guid? ApiKeyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
}
