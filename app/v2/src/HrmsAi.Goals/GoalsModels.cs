namespace HrmsAi.Goals;

public class GoalsSettings
{
    public const string SectionName = "Goals";

    public double Temperature { get; set; } = 0.7;
    public int MaxOutputTokens { get; set; } = 900;
    public int MaxWaitSeconds { get; set; } = 30;
    public string? Model { get; set; }

    // The Groq-style route returns what the AI wrote, cleaned. If your screen expects an object
    // such as { "goals": [ ... ] } instead of a plain array, turn this on.
    public bool WrapArrayAsObject { get; set; }
    public string ObjectKey { get; set; } = "goals";
}

public class GoalRequest
{
    public string? SummaryDescription { get; set; }
    public string? Position { get; set; }
    public string? Department { get; set; }
    public string? SubDepartment { get; set; }
    public string? EmployeeType { get; set; }
    public string? Grade { get; set; }
    public string? GradeLevel { get; set; }
    public int? GoalCount { get; set; }
}

public class GoalItem
{
    public string Goal { get; set; } = "";
    public List<string> Kpis { get; set; } = new();
}
