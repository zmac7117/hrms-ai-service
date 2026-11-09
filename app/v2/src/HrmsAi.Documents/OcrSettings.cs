namespace HrmsAi.Documents;

public class OcrSettings
{
    public const string SectionName = "Ocr";

    public string ToolsRoot { get; set; } = @"C:\HRMS AI\tools";
    public string? TesseractPath { get; set; }      // empty = look under ToolsRoot\tesseract, then the PATH
    public string? PdftoppmPath { get; set; }       // empty = look under ToolsRoot\poppler, then the PATH
    public string? TessdataPath { get; set; }       // empty = the tessdata folder next to tesseract.exe
    public string Language { get; set; } = "eng";   // for example "eng+hin"
    public int PageSegMode { get; set; } = 6;       // 6 = one block of text, keeps invoice rows together
    public int Dpi { get; set; } = 200;             // resolution used for scanned PDF pages
    public int MaxPages { get; set; } = 3;          // pages read per document
    public int TimeoutSeconds { get; set; } = 90;   // per tool call
    public string TempPath { get; set; } = @"C:\HRMS AI\data\temp";
}
