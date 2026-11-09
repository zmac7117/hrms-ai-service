using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace HrmsAi.Documents;

// Reads a PDF line by line and keeps wide gaps between columns,
// so the rows of an invoice table stay together on one line.
public static class PdfLayoutExtractor
{
    public static (string Text, int Pages) Extract(Stream stream, int maxPages)
    {
        using var doc = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        var read = 0;

        foreach (var page in doc.GetPages())
        {
            if (read++ >= maxPages) break;
            var words = page.GetWords().ToList();
            if (words.Count == 0) continue;

            // words on (almost) the same height form one line
            var lines = new List<List<Word>>();
            List<Word>? current = null;
            var lineY = 0.0;
            foreach (var w in words.OrderByDescending(x => x.BoundingBox.Bottom))
            {
                if (current is null || Math.Abs(lineY - w.BoundingBox.Bottom) > 3.0)
                {
                    current = new List<Word>();
                    lines.Add(current);
                    lineY = w.BoundingBox.Bottom;
                }
                current.Add(w);
            }

            foreach (var line in lines)
            {
                var text = new StringBuilder();
                var prevRight = double.NaN;
                foreach (var w in line.OrderBy(x => x.BoundingBox.Left))
                {
                    if (!double.IsNaN(prevRight)) text.Append(w.BoundingBox.Left - prevRight > 14 ? "   " : " ");
                    text.Append(w.Text);
                    prevRight = w.BoundingBox.Right;
                }
                sb.AppendLine(text.ToString());
            }
            sb.AppendLine();
        }
        return (sb.ToString().Trim(), doc.NumberOfPages);
    }
}
