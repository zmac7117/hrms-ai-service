namespace HrmsAi.Resume.Models;

// PUBLIC OUTPUT CONTRACT for resume parsing, schemaVersion 1.0.
// Unknown single values are null. Lists are never null.
public class ParsedResume
{
    public string SchemaVersion { get; set; } = "1.0";
    public CandidateInfo Candidate { get; set; } = new();
    public double? TotalExperienceYears { get; set; }
    public string ExperienceYearsSource { get; set; } = "unknown";   // stated | calculated | unknown
    public List<string> Skills { get; set; } = new();
    public List<ExperienceEntry> Experience { get; set; } = new();
    public List<EducationEntry> Education { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public ParseMeta Meta { get; set; } = new();
}

public class CandidateInfo
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class ResumeDate
{
    public string? Text { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public bool IsCurrent { get; set; }
}

public class ExperienceEntry
{
    public string? Company { get; set; }
    public string? Title { get; set; }
    public ResumeDate? Start { get; set; }
    public ResumeDate? End { get; set; }
    public int? DurationMonths { get; set; }
}

public class EducationEntry
{
    public string? Degree { get; set; }
    public string? Institution { get; set; }
    public int? Year { get; set; }        // completion year (last year found in the text)
    public string? Period { get; set; }   // as written
}

public class ParseMeta
{
    public string Model { get; set; } = "";
    public DateTime ParsedAtUtc { get; set; }
}

