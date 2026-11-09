using System.Globalization;
using System.Text.RegularExpressions;

namespace HrmsAi.Invoices;

// Turns printed amounts, dates and currencies into clean values
public static class InvoiceParsing
{
    // "1,23,456.78", "1.234,56", "1 180,00", "(45.00)" -> decimal with two decimals
    public static decimal? Amount(string? text)
    {
        var v = Parse(text);
        return v is null ? null : Math.Round(v.Value, 2) + 0.00m;
    }

    public static decimal? Quantity(string? text) => Parse(text);

    private static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        var negative = t.StartsWith("-") || (t.StartsWith("(") && t.EndsWith(")"));

        var m = Regex.Match(t, @"\d[\d.,\s]*\d|\d");
        if (!m.Success) return null;
        var s = Regex.Replace(m.Value, @"\s", "");

        var lastDot = s.LastIndexOf('.');
        var lastComma = s.LastIndexOf(',');
        string normalized;
        if (lastDot >= 0 && lastComma >= 0)
        {
            // the separator that comes last is the decimal one
            normalized = lastDot > lastComma ? s.Replace(",", "") : s.Replace(".", "").Replace(',', '.');
        }
        else if (lastComma >= 0)
        {
            var after = s.Length - lastComma - 1;
            var commas = s.Count(c => c == ',');
            normalized = (commas == 1 && after is 1 or 2) ? s.Replace(',', '.') : s.Replace(",", "");
        }
        else
        {
            normalized = s.Count(c => c == '.') > 1 ? s.Replace(".", "") : s;
        }

        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return null;
        return negative ? -d : d;
    }

    public static (string? Code, string? Warning) Currency(string? raw, string? fallback)
    {
        var u = (raw ?? "").Trim().ToUpperInvariant().Replace(".", "");
        if (u.Length > 0)
        {
            if (u.Contains('\u20B9') || u == "RS" || u == "INR" || u.StartsWith("RS ")) return ("INR", null);
            if (u == "$") return ("USD", "The currency was read as USD from the '$' sign. Please confirm it.");
            if (u == "USD" || u == "US$") return ("USD", null);
            if (u.Contains('\u20AC') || u == "EUR") return ("EUR", null);
            if (u.Contains('\u00A3') || u == "GBP") return ("GBP", null);
            if (Regex.IsMatch(u, "^[A-Z]{3}$")) return (u, null);
        }
        if (!string.IsNullOrWhiteSpace(fallback)) return (fallback.Trim().ToUpperInvariant(), null);
        return (null, null);
    }

    // Returns the date as yyyy-MM-dd. Ambiguous = both day and month could be the first number.
    public static (string? Iso, bool Ambiguous) Date(string? text, bool dayFirst)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, false);
        var t = Regex.Replace(text.Trim(), @"\s+", " ");
        t = Regex.Replace(t, @"\s+\d{1,2}:\d{2}(:\d{2})?\s*(AM|PM)?$", "", RegexOptions.IgnoreCase);

        var m = Regex.Match(t, @"^(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})$");
        if (m.Success)
            return Build(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), false);

        m = Regex.Match(t, @"^(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{2,4})$");
        if (m.Success)
        {
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value), y = int.Parse(m.Groups[3].Value);
            if (m.Groups[3].Value.Length == 2) y += 2000;
            int day, month;
            var ambiguous = false;
            if (a > 12) { day = a; month = b; }
            else if (b > 12) { month = a; day = b; }
            else
            {
                ambiguous = a != b;
                if (dayFirst) { day = a; month = b; } else { month = a; day = b; }
            }
            return Build(y, month, day, ambiguous);
        }

        var formats = new[]
        {
            "dd MMM yyyy", "d MMM yyyy", "dd MMMM yyyy", "d MMMM yyyy", "MMM dd, yyyy", "MMM d, yyyy",
            "MMMM dd, yyyy", "MMMM d, yyyy", "dd-MMM-yyyy", "d-MMM-yyyy", "dd-MMM-yy", "d-MMM-yy",
            "MMM dd yyyy", "MMM d yyyy", "dd MMM yy", "d MMM yy", "ddd, dd MMM yyyy"
        };
        if (DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
            return (dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), false);
        return (null, false);
    }

    private static (string? Iso, bool Ambiguous) Build(int y, int month, int day, bool ambiguous)
    {
        if (y < 1990 || y > 2100 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(y, month)) return (null, false);
        return (new DateTime(y, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ambiguous);
    }
}
