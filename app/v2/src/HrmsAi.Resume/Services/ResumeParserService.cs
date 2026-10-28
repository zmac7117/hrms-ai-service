using System.Text.Json;
using HrmsAi.Resume.Models;
using Microsoft.Extensions.AI;
using HrmsAi.Abstractions;
using Microsoft.Extensions.Options;

namespace HrmsAi.Resume.Services;

public class ResumeParserService(IChatClient chat, ILogger<ResumeParserService> log, IOptions<AiOptions> ai)
{
    private const string SystemPrompt = """
        You are a resume parser. Read the resume text and return JSON that matches the schema.

        FIELDS
        - fullName: from the top of the resume. Copy exactly as written.
        - totalExperienceYears: the number of years if the resume states it (for example "6 years"). Otherwise 0.
        - skills: ONE entry per skill. Include every programming language, framework, database, tool and platform
          from the skills section. Also include technologies named in the job descriptions. Split comma-separated
          lists into separate items. Never return an empty skills list if the resume mentions any technology.
        - experience: ONE entry per job (company, title, start, end). Include ALL jobs. Description lines under a
          job are not separate jobs. A headline or summary line is not a job.
        - education: ONE entry per degree with degree, institution and year.

        RULES
        - Copy text exactly as written. Never invent or guess. Keep dates exactly as written
          ("2018", "Jan 2021", "Present"). Do not add months or days.
        - Use an empty string or 0 only when the information is truly not in the resume.
        - List at most 30 skills (the most important ones).
        - Return JSON only.
        """;

    // Worked example: shows the model exactly what a correct answer looks like.
    private const string ExampleResume = """
        Priya Nair
        Backend Engineer
        priya.nair@example.com | +91 91234 56789

        SUMMARY
        Backend engineer with 5 years of experience.

        SKILLS
        Python, Django, PostgreSQL, Docker, AWS

        EXPERIENCE
        Zenith Labs - Backend Engineer (Mar 2020 - Present)
        Built REST APIs with Django and PostgreSQL.
        Orbit Systems - Junior Developer (Jul 2017 - Feb 2020)
        Maintained internal tools in Python.

        EDUCATION
        B.E. Information Technology, Anna University, 2017
        """;

    private static readonly ResumeData ExampleAnswer = new()
    {
        FullName = "Priya Nair",
        Email = "priya.nair@example.com",
        Phone = "+91 91234 56789",
        TotalExperienceYears = 5,
        Skills = new() { "Python", "Django", "PostgreSQL", "Docker", "AWS", "REST APIs" },
        Experience = new()
        {
            new() { Company = "Zenith Labs", Title = "Backend Engineer", Start = "Mar 2020", End = "Present" },
            new() { Company = "Orbit Systems", Title = "Junior Developer", Start = "Jul 2017", End = "Feb 2020" }
        },
        Education = new()
        {
            new() { Degree = "B.E. Information Technology", Institution = "Anna University", Year = "2017" }
        }
    };

    private static readonly string ExampleAnswerJson =
        JsonSerializer.Serialize(ExampleAnswer, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public async Task<ParseOutcome> ParseAsync(string text, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, ExampleResume),
            new(ChatRole.Assistant, ExampleAnswerJson),
            new(ChatRole.User, text)
        };
        var options = new ChatOptions { Temperature = 0 };

        var response = await chat.GetResponseAsync<ResumeData>(messages, options, cancellationToken: ct);

        if (!response.TryGetResult(out var result))
        {
            log.LogWarning("Model returned unparseable output: {Text}", response.Text);
            throw new InvalidOperationException("The AI returned output that could not be read as resume data.");
        }
        return new ParseOutcome(ResumeNormalizer.Normalize(result, text, ai.Value.ChatModel), AiUsage.From(response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, text.Length));
    }
}









