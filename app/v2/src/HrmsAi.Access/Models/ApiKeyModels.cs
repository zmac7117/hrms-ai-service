namespace HrmsAi.Access.Models;

public class ApiKeyRecord
{
    public Guid Id { get; set; }
    public string ClientName { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public byte[] KeyHash { get; set; } = Array.Empty<byte>();
    public bool IsActive { get; set; }
    public string AllowedModules { get; set; } = "*";
    public long? TokenLimit { get; set; }
    public string LimitPeriod { get; set; } = "Monthly";
    public DateTime? ExpiresAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class ApiKeyRow : ApiKeyRecord
{
    public long Used { get; set; }
}

public class QuotaState
{
    public long? TokenLimit { get; set; }
    public string LimitPeriod { get; set; } = "Monthly";
    public bool IsActive { get; set; }
    public long Used { get; set; }
}

public class UsageDay
{
    public DateTime Day { get; set; }
    public long Tokens { get; set; }
    public int Requests { get; set; }
}

public class UsageRow
{
    public DateTime At { get; set; }
    public string? Module { get; set; }
    public string? Operation { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
