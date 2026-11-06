using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HrmsAi.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HrmsAi.ResumeMatch;

// One AI call per candidate. Numbers that code can check or calculate are not left to the AI.
public class MatchScorer(IChatClient chat, IOptions<MatchSettings> options, IOptions<AiOptions> ai)
{
    // How much each part counts in the overall score (the weights add up to 1.0)
    private static readonly (string Key, double Weight)[] Weights =
    {
        ("skills_match", 0.25), ("experience_match", 0.20), ("technical_stack_match", 0.20), ("education_match", 0.10),
        ("soft_skills_match", 0.05), ("cultural_fit_potential", 0.05), ("stability_score", 0.05),
        ("language_proficiency_match", 0.05), ("location_preference_match", 0.05)
    };

    public async Task<(string ResultJson, AiUsage Usage)> ScoreAsync(MatchInput input, CancellationToken ct)
    {
        var s = options.Value;
        var resume = Cut(input.ResumeText, s.MaxResumeChars);
        var job = Cut(input.JobDescription, s.MaxJobChars);
        var language = string.IsNullOrWhiteSpace(input.Language) ? "English" : input.Language.Trim();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, MatchPrompt.System(language)),
            new(ChatRole.User, MatchPrompt.User(resume, job))
        };
        var chatOptions = new ChatOptions { Temperature = (float)s.Temperature, MaxOutputTokens = s.MaxOutputTokens };
        if (!string.IsNullOrWhiteSpace(s.Model)) chatOptions.ModelId = s.Model;

        var response = await chat.GetResponseAsync<MatchAssessment>(messages, chatOptions, cancellationToken: ct);
        if (!response.TryGetResult(out var a))
            throw new InvalidOperationException("The AI returned an answer that could not be read. Please score this candidate again.");

        // Required skills are checked against the real resume text
        var required = a.RequiredSkills.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList();
        var matched = required.Where(x => InText(input.ResumeText, x)).ToList();
        var missing = required.Where(x => !matched.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();

        var parts = new Dictionary<string, int>
        {
            ["skills_match"] = required.Count > 0 ? (int)Math.Round(100.0 * matched.Count / required.Count) : Clamp(a.Skills.Score),
            ["experience_match"] = Clamp(a.Experience.Score),
            ["education_match"] = Clamp(a.Education.Score),
            ["technical_stack_match"] = Clamp(a.TechnicalStack.Score),
            ["soft_skills_match"] = Clamp(a.SoftSkills.Score),
            ["cultural_fit_potential"] = Clamp(a.CulturalFit.Score),
            ["stability_score"] = Clamp(a.Stability.Score),
            ["language_proficiency_match"] = Clamp(a.Language.Score),
            ["location_preference_match"] = Clamp(a.Location.Score)
        };
        var overall = (int)Math.Round(Weights.Sum(w => parts[w.Key] * w.Weight));

        var scores = new Dictionary<string, int> { ["overall_match"] = overall };
        foreach (var kv in parts) scores[kv.Key] = kv.Value;

        string Safe(string? t)
        {
            var text = string.IsNullOrWhiteSpace(t) ? "No explanation was given." : t.Trim();
            return s.EncodeHtml ? WebWebUtility(text) : text;
        }

        var skillsReason = Safe(a.Skills.Why);
        if (required.Count > 0)
        {
            skillsReason += $" Found {matched.Count} of {required.Count} required skills.";
            if (matched.Count > 0) skillsReason += $" Found: {Safe(string.Join(", ", matched))}.";
            if (missing.Count > 0) skillsReason += $" Missing: {Safe(string.Join(", ", missing))}.";
        }

        var explanations = new Dictionary<string, string>
        {
            ["overall_match"] = Safe(a.Summary),
            ["skills_match"] = skillsReason,
            ["experience_match"] = Safe(a.Experience.Why),
            ["education_match"] = Safe(a.Education.Why),
            ["technical_stack_match"] = Safe(a.TechnicalStack.Why),
            ["soft_skills_match"] = Safe(a.SoftSkills.Why),
            ["cultural_fit_potential"] = Safe(a.CulturalFit.Why),
            ["stability_score"] = Safe(a.Stability.Why),
            ["language_proficiency_match"] = Safe(a.Language.Why),
            ["location_preference_match"] = Safe(a.Location.Why)
        };

        var result = new Dictionary<string, object?>
        {
            ["match_scores"] = scores,
            ["explanations"] = explanations,
            ["summary"] = Safe(a.Summary),
            ["matched_skills"] = matched.Select(x => s.EncodeHtml ? WebWebUtility(x) : x).ToList(),
            ["missing_skills"] = missing.Select(x => s.EncodeHtml ? WebWebUtility(x) : x).ToList(),
            ["meta"] = new { model = s.Model ?? ai.Value.ChatModel, scored_at_utc = DateTime.UtcNow }
        };

        var usage = AiUsage.From(response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, resume.Length + job.Length);
        return (JsonSerializer.Serialize(result), usage);
    }

    private static string WebWebUtility(string text) => WebUtility.HtmlEncode(text);

    private static string Cut(string text, int max) => text.Length <= max ? text : text.Substring(0, max);

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);

    private static bool InText(string text, string skill)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(skill)) return false;

        var pattern = $@"(?<![\w#+.]){Regex.Escape(skill)}(?![\w#+])";
        if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)) return true;

        // ".NET" is often written inside "ASP.NET"
        if (skill.StartsWith('.') && skill.Length > 1)
            return Regex.IsMatch(text, $@"(?<![\w#+]){Regex.Escape(skill.TrimStart('.'))}(?![\w#+])", RegexOptions.IgnoreCase);
        return false;
    }
}
