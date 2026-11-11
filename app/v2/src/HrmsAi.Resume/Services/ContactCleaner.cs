using System.Text;
using System.Text.RegularExpressions;
using HrmsAi.Resume.Models;

namespace HrmsAi.Resume.Services;

// Repairs contact details when the text reader glued neighbouring words together,
// for example "name@example.comlinkedin" or "1494name@example.com".
public static class ContactCleaner
{
    private static readonly Regex EmailRx = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+", RegexOptions.Compiled);
    private static readonly Regex PhoneRx = new(@"\+?\d[\d\s().\-]{6,}\d", RegexOptions.Compiled);
    private static readonly Regex YearRunRx = new(@"^((19|20)\d{2}[\s\-\u2013.]*)+$", RegexOptions.Compiled);

    // endings that are real, even though they start like a shorter one ("consulting" starts with "co")
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "biz", "online", "tech", "network", "technology", "company", "consulting", "community", "college", "computer",
        "coach", "codes", "coffee", "contractors", "cool", "coop", "country", "courses", "institute", "international",
        "industries", "investments", "insure", "media", "menu", "design", "digital", "deals", "direct", "directory",
        "discount", "doctor", "domains", "audio", "auction", "auto", "capital", "care", "career", "careers", "cars",
        "cash", "catering", "center", "sale", "salon", "solutions", "services", "systems", "global", "group", "finance"
    };

    // short endings a glued word usually follows
    private static readonly string[] Prefixes = new[]
        {
        "com", "net", "org", "edu", "gov", "info", "dev", "app",
        "tech", "biz", "ai", "io", "co", "in", "ae", "uk", "us",
        "ca", "au", "de", "fr", "me", "pk", "lk", "qa", "sa",
        "om", "kw", "bh", "sg", "my", "ph", "ng", "za", "ke"
    }
        .OrderByDescending(x => x.Length)
        .ToArray();
    // web sites that follow an address on a contact line
    private static readonly HashSet<string> SiteWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "linkedin", "github", "gitlab", "facebook", "twitter", "instagram", "behance", "skype", "whatsapp",
        "telegram", "portfolio", "website", "medium", "youtube", "dribbble", "stackoverflow", "kaggle"
    };

    private sealed record Glued(string Prefix, string Email, string Suffix);

    // ---------- used before the AI reads the text ----------
    public static string Prepare(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var phoneDigits = DigitsOnly(FindPhones(text).FirstOrDefault() ?? "");

        var sb = new StringBuilder();
        var last = 0;
        foreach (Match m in EmailRx.Matches(text))
        {
            var parts = Split(m.Value, phoneDigits, Before(text, m.Index));
            if (parts is null) continue;
            sb.Append(text, last, m.Index - last);
            if (parts.Prefix.Length > 0) sb.Append(parts.Prefix).Append(' ');
            sb.Append(parts.Email);
            if (parts.Suffix.Length > 0) sb.Append(' ').Append(parts.Suffix);
            last = m.Index + m.Length;
        }
        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }

    // ---------- used after the AI answered ----------
    public static void Repair(ParsedResume p, string text, string? aiEmail, string? aiPhone)
    {
        // phone first: the email check needs it
        var phones = FindPhones(text);
        var phone = ChoosePhone(CleanPhone(p.Candidate.Phone) ?? CleanPhone(aiPhone), phones);
        var phoneDigits = DigitsOnly(phone ?? "");

        var candidates = FindEmails(text, phoneDigits);
        var wanted = CleanEmail(p.Candidate.Email, phoneDigits, text) ?? CleanEmail(aiEmail, phoneDigits, text);
        var email = candidates.FirstOrDefault(c => string.Equals(c, wanted, StringComparison.OrdinalIgnoreCase))
                    ?? candidates.FirstOrDefault()
                    ?? wanted;
        email = email?.ToLowerInvariant();

        if (email is not null && p.Candidate.Email is not null &&
            !string.Equals(email, p.Candidate.Email, StringComparison.OrdinalIgnoreCase))
            p.Warnings.Add("The email address was corrected (extra characters removed). Please check it.");
        p.Candidate.Email = email;
        if (email is not null) p.Warnings.RemoveAll(w => w == "No email found.");
        else if (!p.Warnings.Contains("No email found.")) p.Warnings.Add("No email found.");

        if (phone is not null && p.Candidate.Phone is not null &&
            DigitsOnly(phone) != DigitsOnly(p.Candidate.Phone))
            p.Warnings.Add("The phone number was corrected (extra characters removed). Please check it.");
        p.Candidate.Phone = phone;
        if (phone is not null) p.Warnings.RemoveAll(w => w == "No phone number found.");
        else if (!p.Warnings.Contains("No phone number found.")) p.Warnings.Add("No phone number found.");
    }

    // ---------- emails ----------
    private static List<string> FindEmails(string text, string phoneDigits)
    {
        var list = new List<string>();
        foreach (Match m in EmailRx.Matches(text))
        {
            var parts = Split(m.Value, phoneDigits, Before(text, m.Index));
            if (parts is not null && !list.Contains(parts.Email, StringComparer.OrdinalIgnoreCase)) list.Add(parts.Email);
        }
        return list;
    }

    private static string? CleanEmail(string? s, string phoneDigits, string text)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = EmailRx.Match(s);
        if (!m.Success) return null;
        var index = text.IndexOf(m.Value, StringComparison.OrdinalIgnoreCase);
        return Split(m.Value, phoneDigits, index >= 0 ? Before(text, index) : "")?.Email;
    }

    private static Glued? Split(string match, string phoneDigits, string before)
    {
        var at = match.IndexOf('@');
        if (at <= 0 || at >= match.Length - 1) return null;
        var local = match.Substring(0, at);
        var labels = match.Substring(at + 1).Split('.', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim('-')).ToList();
        if (labels.Count < 2) return null;

        var prefix = "";

        // a label glued in front: "Emailarjun.menon@..."
        var label = Regex.Match(local, @"^e-?mail(?=[A-Za-z0-9])", RegexOptions.IgnoreCase);
        if (label.Success && local.Length - label.Length >= 3)
        {
            prefix = label.Value;
            local = local.Substring(label.Length);
        }

        // the end of the phone number glued in front: "1494kamran@..."
        var digits = Regex.Match(local, @"^(\d{3,6})(?=[A-Za-z])");
        if (digits.Success && phoneDigits.Length > 0 && SpansPhone(before, digits.Value, phoneDigits))
        {
            prefix += digits.Value;
            local = local.Substring(digits.Length);
        }
        if (local.Length == 0) return null;

        var suffix = "";

        // a web address glued behind: "name@example.comlinkedin.com"
        for (var i = 1; i < labels.Count - 1 && suffix.Length == 0; i++)
        {
            foreach (var t in new[] { "com", "net", "org" })
            {
                if (labels[i].StartsWith(t, StringComparison.OrdinalIgnoreCase) && SiteWords.Contains(labels[i].Substring(t.Length)))
                {
                    suffix = labels[i].Substring(t.Length) + "." + string.Join(".", labels.Skip(i + 1));
                    labels = labels.Take(i).Append(t).ToList();
                    break;
                }
            }
        }

        // a word glued behind the ending: "gmail.comphone"
        var lastLabel = labels[^1];
        if (suffix.Length == 0 && lastLabel.Length >= 5 && !Known.Contains(lastLabel))
        {
            foreach (var t in Prefixes)
            {
                if (lastLabel.StartsWith(t, StringComparison.OrdinalIgnoreCase) && lastLabel.Length - t.Length >= 2)
                {
                    suffix = lastLabel.Substring(t.Length);
                    labels[^1] = lastLabel.Substring(0, t.Length);
                    break;
                }
            }
        }

        if (!Regex.IsMatch(labels[^1], @"^[A-Za-z]{2,}$")) return null;
        return new Glued(prefix, local + "@" + string.Join(".", labels), suffix);
    }

    // do the digits come from the phone number written just before?  "+971 56 712 " + "1494" = the whole phone number
    private static bool SpansPhone(string before, string digitRun, string phoneDigits) =>
        (DigitsOnly(before) + digitRun).EndsWith(phoneDigits, StringComparison.Ordinal) &&
        Regex.IsMatch(before, @"\d[\s.\-]*$");

    private static string Before(string text, int index) =>
        text.Substring(Math.Max(0, index - 30), Math.Min(30, index));

    // ---------- phone numbers ----------
    private static List<string> FindPhones(string text)
    {
        var list = new List<string>();
        foreach (Match m in PhoneRx.Matches(text))
        {
            var v = Regex.Replace(m.Value, @"\s+", " ").Trim();
            var digits = DigitsOnly(v).Length;
            if (digits < 8 || digits > 15 || YearRunRx.IsMatch(v)) continue;
            list.Add(v.Trim('-', '.', ' ', '('));
            if (list.Count >= 20) break;
        }
        return list;
    }

    // only the phone-like part at the start, up to the first letter
    private static string? CleanPhone(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = Regex.Replace(s.Trim(), @"^[^\d+(]+", "");
        var m = Regex.Match(t, @"^\+?[\d\s().\-]+");
        if (!m.Success) return null;
        t = Regex.Replace(m.Value, @"\s+", " ").Trim(' ', '-', '.');
        var d = DigitsOnly(t).Length;
        return d >= 7 && d <= 15 ? t : null;
    }

    private static string? ChoosePhone(string? cleaned, List<string> found)
    {
        if (cleaned is null) return found.FirstOrDefault();
        var d = DigitsOnly(cleaned);
        if (found.Any(f => DigitsOnly(f) == d)) return cleaned;
        var related = found.FirstOrDefault(f => { var fd = DigitsOnly(f); return fd.Contains(d) || d.Contains(fd); });
        return related ?? found.FirstOrDefault() ?? cleaned;
    }

    private static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());
}
