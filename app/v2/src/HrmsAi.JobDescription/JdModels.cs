namespace HrmsAi.JobDescription;

public class JdSettings
{
    public const string SectionName = "Jd";

    public double Temperature { get; set; } = 0.7;
    public int MaxOutputTokens { get; set; } = 900;
    public int MaxWaitSeconds { get; set; } = 30;      // longest a call waits for a free AI slot
    public string? Model { get; set; }                  // empty = the default model
}

public class JdRequest
{
    public string? JobTitle { get; set; }
    public string? Position { get; set; }
    public string? Company { get; set; }
    public string? CompanyDescription { get; set; }
    public string? Department { get; set; }
    public string? SubDepartment { get; set; }
    public string? Grade { get; set; }
    public string? GradeLevel { get; set; }
    public string? Location { get; set; }
    public string? EmployeeType { get; set; }
    public string? JobType { get; set; }
    public string? YearsOfExperience { get; set; }
    public string? Certifications { get; set; }
    public string? Nationality { get; set; }
}
