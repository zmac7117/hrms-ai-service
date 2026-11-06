using System.ComponentModel;

namespace HrmsAi.ResumeMatch;

public class MatchSettings
{
    public const string SectionName = "Match";

    public double Temperature { get; set; } = 0;
    public int MaxOutputTokens { get; set; } = 900;
    public int MaxResumeChars { get; set; } = 12000;     // longer resumes are cut (about 3,000 tokens)
    public int MaxJobChars { get; set; } = 6000;
    public string? Model { get; set; }                    // empty = the default model

    // The HRMS screen inserts the explanations into the page as HTML, so the text is HTML-encoded.
    public bool EncodeHtml { get; set; } = true;

    // Only needed when a proxy hides the public address from the service
    public string? PublicBaseUrl { get; set; }
}

// What a queued match job carries
public record MatchInput(string ResumeText, string JobDescription, string Language);

public record SubmitResult(bool Ok, int StatusCode, string? Error, Guid JobId);

public class Dimension
{
    [Description("Whole number from 0 to 100.")]
    public int Score { get; set; }

    [Description("The reason, in under 25 words.")]
    public string Why { get; set; } = "";
}

// The shape the AI must answer in
public class MatchAssessment
{
    [Description("Skills, tools, technologies and qualifications the job description REQUIRES. Short names such as 'C#' or 'SQL Server'. At most 15.")]
    public List<string> RequiredSkills { get; set; } = new();

    public Dimension Skills { get; set; } = new();
    public Dimension Experience { get; set; } = new();
    public Dimension Education { get; set; } = new();
    public Dimension TechnicalStack { get; set; } = new();
    public Dimension SoftSkills { get; set; } = new();
    public Dimension CulturalFit { get; set; } = new();
    public Dimension Stability { get; set; } = new();
    public Dimension Language { get; set; } = new();
    public Dimension Location { get; set; } = new();

    [Description("The overall judgement in at most 30 words.")]
    public string Summary { get; set; } = "";
}
