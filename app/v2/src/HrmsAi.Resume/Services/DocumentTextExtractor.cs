using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HrmsAi.Resume.Services;

public static class DocumentTextExtractor
{
    public static string Extract(Stream stream, string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf"  => PdfTextExtractor.Extract(stream),
        ".docx" => ExtractDocx(stream),
        _       => throw new NotSupportedException($"Unsupported file type '{extension}'.")
    };

    private static string ExtractDocx(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart;
        if (main is null) return "";

        var sb = new StringBuilder();
        // Contact details are often placed in the page header
        foreach (var h in main.HeaderParts) AppendParagraphs(h.Header, sb);
        AppendParagraphs(main.Document?.Body, sb);
        foreach (var f in main.FooterParts) AppendParagraphs(f.Footer, sb);
        return sb.ToString();
    }

    private static void AppendParagraphs(OpenXmlElement? root, StringBuilder sb)
    {
        if (root is null) return;
        foreach (var p in root.Descendants<Paragraph>())
        {
            // Word stores text boxes twice (a Choice and a Fallback copy); skip the copy
            if (p.Ancestors().Any(a => a.LocalName == "Fallback")) continue;

            var line = new StringBuilder();
            // Only this paragraph's own text, not text of paragraphs nested in it
            foreach (var el in p.Descendants().Where(e => e.Ancestors<Paragraph>().First() == p))
            {
                switch (el)
                {
                    case Text t: line.Append(t.Text); break;
                    case TabChar: line.Append('\t'); break;
                    case Break: line.Append('\n'); break;
                }
            }
            if (line.ToString().Trim().Length > 0) sb.AppendLine(line.ToString());
        }
    }
}

