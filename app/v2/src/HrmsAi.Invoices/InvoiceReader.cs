using HrmsAi.Documents;
using Microsoft.Extensions.Options;

namespace HrmsAi.Invoices;

// Gets the text of the document: straight from a PDF that has text, otherwise with OCR.
public class InvoiceReader(OcrEngine ocr, IOptions<OcrSettings> ocrOptions, IOptions<InvoiceSettings> options)
{
    public async Task<(string Text, string Source, int Pages)> ReadAsync(string path, string kind, CancellationToken ct)
    {
        var maxChars = options.Value.MaxTextChars;

        if (kind == "pdf")
        {
            string text;
            int pages;
            await using (var stream = File.OpenRead(path))
                (text, pages) = PdfLayoutExtractor.Extract(stream, ocrOptions.Value.MaxPages);

            // a scanned PDF has (almost) no text of its own
            if (text.Count(char.IsLetterOrDigit) >= 30) return (Cap(text, maxChars), "pdf-text", pages);

            var scanned = await ocr.ReadPdfAsync(path, ct);
            return (Cap(scanned.Text, maxChars), "ocr", scanned.Pages);
        }

        var image = await ocr.ReadImageAsync(path, ct);
        return (Cap(image.Text, maxChars), "ocr", 1);
    }

    private static string Cap(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
