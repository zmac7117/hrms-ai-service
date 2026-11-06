using System.Net;
using System.Text.RegularExpressions;

namespace HrmsAi.JobDescription;

// Small models sometimes wrap HTML in code fences, add a sentence before it, or use **bold**.
public static class HtmlCleaner
{
    public static string Clean(string text)
    {
        var t = (text ?? "").Replace("```html", "").Replace("```", "").Trim();
        t = Regex.Replace(t, @"\*\*(.+?)\*\*", "<strong>$1</strong>");

        var first = t.IndexOf('<');
        var last = t.LastIndexOf('>');
        if (first < 0 || last <= first)
        {
            // plain text: turn paragraphs into <p>
            var paragraphs = t.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Concat(paragraphs.Select(p => "<p>" + WebUtility.HtmlEncode(p) + "</p>"));
        }
        return t.Substring(first, last - first + 1);
    }
}
