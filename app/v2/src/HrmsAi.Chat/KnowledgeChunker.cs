using System.Text;
using System.Text.RegularExpressions;

namespace HrmsAi.Chat;

// Cuts a text into small pieces, keeping paragraphs together.
public static class KnowledgeChunker
{
    public static List<string> Split(string text, int maxChars)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var chunks = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            var s = current.ToString().Trim();
            if (s.Length > 0) chunks.Add(s);
            current.Clear();
        }

        foreach (var paragraph in Regex.Split(text, @"\n\s*\n").Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            foreach (var piece in HardSplit(paragraph, maxChars))
            {
                if (current.Length > 0 && current.Length + piece.Length + 2 > maxChars) Flush();
                if (current.Length > 0) current.Append("\n\n");
                current.Append(piece);
            }
        }
        Flush();
        return chunks;
    }

    private static IEnumerable<string> HardSplit(string paragraph, int max)
    {
        if (paragraph.Length <= max) { yield return paragraph; yield break; }

        var buffer = new StringBuilder();
        foreach (var sentence in Regex.Split(paragraph, @"(?<=[.!?])\s+"))
        {
            var part = sentence;
            while (part.Length > max)
            {
                if (buffer.Length > 0) { yield return buffer.ToString(); buffer.Clear(); }
                yield return part[..max];
                part = part[max..];
            }
            if (part.Length == 0) continue;
            if (buffer.Length > 0 && buffer.Length + part.Length + 1 > max)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
            if (buffer.Length > 0) buffer.Append(' ');
            buffer.Append(part);
        }
        if (buffer.Length > 0) yield return buffer.ToString();
    }
}