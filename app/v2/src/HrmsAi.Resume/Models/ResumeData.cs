using System.ComponentModel;

namespace HrmsAi.Resume.Models;

public class ResumeData
{
    [Description("Candidate's full name, usually the first line of the resume.")]
    public string FullName { get; set; } = "";

    [Description("Email address from the resume. Empty string if none.")]
    [System.Text.Json.Serialization.JsonIgnore]
    public string Email { get; set; } = "";

    [Description("Phone number exactly as written. Empty string if none.")]
    [System.Text.Json.Serialization.JsonIgnore]
    public string Phone { get; set; } = "";

    [Description("Total years of experience if the resume states it (e.g. '6 years'). 0 if not stated.")]
    public double TotalExperienceYears { get; set; }

    [Description("Every technology, tool, language or skill listed in the resume, one item per skill.")]
    public List<string> Skills { get; set; } = new();

    [Description("Real jobs only. One entry per employer and role. Do not include summary or headline lines.")]
    public List<ExperienceItem> Experience { get; set; } = new();

    [Description("Every degree or qualification: degree name, institution and year.")]
    public List<EducationItem> Education { get; set; } = new();
}

public class ExperienceItem
{
    [Description("Employer or company name.")]
    public string Company { get; set; } = "";

    [Description("Job title at that company.")]
    public string Title { get; set; } = "";

    [Description("Start date exactly as written in the resume, e.g. '2018' or 'Jan 2018'. Do not add month or day.")]
    public string Start { get; set; } = "";

    [Description("End date exactly as written, or 'Present'.")]
    public string End { get; set; } = "";
}

public class EducationItem
{
    [Description("Degree or qualification name, e.g. 'B.Tech Computer Science'.")]
    public string Degree { get; set; } = "";

    [Description("University, college or school name.")]
    public string Institution { get; set; } = "";

    [Description("Year of completion as written.")]
    public string Year { get; set; } = "";
}


