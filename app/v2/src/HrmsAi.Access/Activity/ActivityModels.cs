namespace HrmsAi.Access.Activity;

public class ActivityRow
{
    public string Id { get; set; } = "";            // "job:<guid>" or "call:<number>"
    public string Source { get; set; } = "";
    public DateTime At { get; set; }
    public string Module { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Status { get; set; } = "";
    public int? DurationMs { get; set; }
    public string? Title { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public string? Error { get; set; }
    public Guid? ApiKeyId { get; set; }
    public string? ClientName { get; set; }
}

public class ActivityDetail : ActivityRow
{
    public DateTime? CompletedAt { get; set; }
    public string? Result { get; set; }
}

public class ModuleStat
{
    public string Module { get; set; } = "";
    public int Requests { get; set; }
    public long Tokens { get; set; }
    public int Failed { get; set; }
    public double? AvgSeconds { get; set; }
}

public class DayStat
{
    public DateTime Day { get; set; }
    public int Requests { get; set; }
    public long Tokens { get; set; }
}

public class ActivitySummary
{
    public int Days { get; set; }
    public int Requests { get; set; }
    public long Tokens { get; set; }
    public int Failed { get; set; }
    public double? AvgSeconds { get; set; }
    public List<ModuleStat> ByModule { get; set; } = new();
    public List<DayStat> ByDay { get; set; } = new();
}
