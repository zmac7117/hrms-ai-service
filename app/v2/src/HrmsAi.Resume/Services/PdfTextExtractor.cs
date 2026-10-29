using UglyToad.PdfPig;

namespace HrmsAi.Resume.Services;

public static class PdfTextExtractor
{
    public static string Extract(Stream stream)
    {
        using var doc = PdfDocument.Open(stream);
        return string.Join("\n", doc.GetPages().Select(p => p.Text));
    }
}
//Hamid Done

