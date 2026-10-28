using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HrmsAi.Resume.Models;

namespace HrmsAi.Resume.Services;

// Turns the raw AI extraction (ResumeData) into the fixed public contract (ParsedResume).
// All rules here are plain code, so the output shape never depends on the AI's mood.
public static class ResumeNormalizer
{
    private static readonly Regex EmailRx = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex PhoneRx = new(@"\+?\d[\d\s().\-]{7,}\d", RegexOptions.Compiled);
    private static readonly Regex YearRunRx = new(@"^((19|20)\d{2}[\s\-–.]*)+$", RegexOptions.Compiled);
    private static readonly Regex YearRx = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex CurrentRx = new(@"\b(present|current|currently|now|till date|to date|ongoing|today)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] MonthNames = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    public static ParsedResume Normalize(ResumeData r, string raw, string model)
    {
        raw ??= "";
        var p = new ParsedResume { Meta = new ParseMeta { Model = model, ParsedAtUtc = DateTime.UtcNow } };

        // ---- candidate ----
        p.Candidate.FullName = NullIfEmpty(Collapse(r.FullName));
        if (p.Candidate.FullName is null) p.Warnings.Add("No name found.");

        var email = NullIfEmpty(r.Email?.Trim());
        if (email is not null && !(EmailRx.IsMatch(email) && raw.Contains(email, StringComparison.OrdinalIgnoreCase)))
        {
            email = null;
            p.Warnings.Add("The AI email was not found in the resume text and was replaced.");
        }
        email ??= EmailRx.Match(raw) is { Success: true } m ? m.Value : null;
        p.Candidate.Email = email?.ToLowerInvariant();
        if (p.Candidate.Email is null) p.Warnings.Add("No email found.");

        var phone = NullIfEmpty(Collapse(r.Phone));
        if (phone is not null)
        {
            var d = DigitsOnly(phone);
            if (d.Length < 7 || d.Length > 15 || !DigitsOnly(raw).Contains(d)) phone = null;
        }
        phone ??= FindPhone(raw);
        p.Candidate.Phone = phone;
        if (phone is null) p.Warnings.Add("No phone number found.");

        // ---- skills ----
        p.Skills = (r.Skills ?? new())
            .SelectMany(s => (s ?? "").Split(new[] { ',', ';', '|', '•' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(Collapse).Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
        if (p.Skills.Count == 0) p.Warnings.Add("No skills found.");

        // ---- experience ----
        var seen = new HashSet<string>();
        var intervals = new List<(int s, int e)>();
        foreach (var e in r.Experience ?? new())
        {
            var company = NullIfEmpty(Collapse(e.Company));
            var title = NullIfEmpty(Collapse(e.Title));
            if (company is null && title is null) continue;

            var start = ParseDate(e.Start);
            var end = ParseDate(e.End);
            if (!seen.Add($"{company}|{title}|{start?.Text}".ToLowerInvariant())) continue;

            int? months = null;
            if (TryIndexes(start, end, out var si, out var ei) && ei >= si)
            {
                months = ei - si + 1;
                intervals.Add((si, ei));
            }
            p.Experience.Add(new ExperienceEntry { Company = company, Title = title, Start = start, End = end, DurationMonths = months });
        }
        if (p.Experience.Count == 0) p.Warnings.Add("No work experience found.");

        // ---- total experience ----
        if (r.TotalExperienceYears is > 0 and <= 60)
        {
            p.TotalExperienceYears = r.TotalExperienceYears;
            p.ExperienceYearsSource = "stated";
        }
        else if (intervals.Count > 0)
        {
            p.TotalExperienceYears = Math.Round(UnionMonths(intervals) / 12.0, 1);
            p.ExperienceYearsSource = "calculated";
        }

        // ---- education ----
        foreach (var ed in r.Education ?? new())
        {
            var degree = NullIfEmpty(Collapse(ed.Degree));
            var inst = NullIfEmpty(Collapse(ed.Institution));
            if (degree is null && inst is null) continue;
            var period = NullIfEmpty(Collapse(ed.Year));
            var years = period is null ? null : YearRx.Matches(period);
            int? year = years is { Count: > 0 } ? int.Parse(years[^1].Value, CultureInfo.InvariantCulture) : null;
            p.Education.Add(new EducationEntry { Degree = degree, Institution = inst, Year = year, Period = period });
        }
        return p;
    }

    // The match score still works on the simple extraction model.
    public static ResumeData ToResumeData(ParsedResume p) => new()
    {
        FullName = p.Candidate.FullName ?? "",
        Email = p.Candidate.Email ?? "",
        Phone = p.Candidate.Phone ?? "",
        TotalExperienceYears = p.TotalExperienceYears ?? 0,
        Skills = p.Skills.ToList(),
        Experience = p.Experience.Select(e => new ExperienceItem
        {
            Company = e.Company ?? "", Title = e.Title ?? "", Start = e.Start?.Text ?? "", End = e.End?.Text ?? ""
        }).ToList(),
        Education = p.Education.Select(e => new EducationItem
        {
            Degree = e.Degree ?? "", Institution = e.Institution ?? "", Year = e.Period ?? ""
        }).ToList()
    };

    // Reads stored job results in both the new format and the older (pre-contract) format.
    public static ResumeData LoadForMatching(string json, JsonSerializerOptions opts)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("schemaVersion", out _)
            ? ToResumeData(JsonSerializer.Deserialize<ParsedResume>(json, opts)!)
            : JsonSerializer.Deserialize<ResumeData>(json, opts)!;
    }

    // ---------- helpers ----------
    private static string Collapse(string? s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());

    private static string? FindPhone(string raw)
    {
        foreach (Match m in PhoneRx.Matches(raw))
        {
            var v = m.Value.Trim();
            var digits = DigitsOnly(v).Length;
            if (digits >= 9 && digits <= 15 && !YearRunRx.IsMatch(v)) return Collapse(v);
        }
        return null;
    }

    private static ResumeDate? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = Collapse(text);
        var d = new ResumeDate { Text = t };
        if (CurrentRx.IsMatch(t)) { d.IsCurrent = true; return d; }

        var y = YearRx.Match(t);
        if (y.Success) d.Year = int.Parse(y.Value, CultureInfo.InvariantCulture);

        var lower = t.ToLowerInvariant();
        for (int i = 0; i < 12; i++)
            if (Regex.IsMatch(lower, $@"\b{MonthNames[i]}")) { d.Month = i + 1; break; }
        if (d.Month is null)
        {
            var nm = Regex.Match(t, @"\b(0?[1-9]|1[0-2])[/\-.](19|20)\d{2}\b");
            if (nm.Success) d.Month = int.Parse(nm.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return d;
    }

    // Month index = year*12 + month. Missing months: start = January, end = December.
    private static bool TryIndexes(ResumeDate? s, ResumeDate? e, out int si, out int ei)
    {
        si = ei = 0;
        if (s?.Year is null) return false;
        si = s.Year.Value * 12 + (s.Month ?? 1);
        if (e is null) return false;
        if (e.IsCurrent) { var n = DateTime.UtcNow; ei = n.Year * 12 + n.Month; return true; }
        if (e.Year is null) return false;
        ei = e.Year.Value * 12 + (e.Month ?? 12);
        return true;
    }

    private static int UnionMonths(List<(int s, int e)> iv)
    {
        int total = 0, curS = 0, curE = -1;
        foreach (var (s, e) in iv.OrderBy(x => x.s))
        {
            if (curE < 0) { curS = s; curE = e; }
            else if (s <= curE + 1) curE = Math.Max(curE, e);
            else { total += curE - curS + 1; curS = s; curE = e; }
        }
        return curE < 0 ? 0 : total + (curE - curS + 1);
    }
}

