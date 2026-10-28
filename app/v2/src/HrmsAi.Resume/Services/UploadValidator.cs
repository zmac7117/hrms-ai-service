using System.Text;

namespace HrmsAi.Resume.Services;

// Checks an upload before any parsing happens
public static class UploadValidator
{
    public static readonly string[] AllowedExtensions = { ".pdf", ".docx" };

    // Returns an error message, or null when the file is acceptable.
    public static string? ValidateBasics(IFormFile file, long maxBytes, out string ext)
    {
        ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (file.Length == 0 || file.Length > maxBytes)
            return $"File must be between 1 byte and {maxBytes / (1024 * 1024)} MB.";
        if (ext == ".doc")
            return "Old .doc files are not supported. Please save the resume as .docx or PDF.";
        if (!AllowedExtensions.Contains(ext))
            return "Only PDF and DOCX files are supported.";
        return null;
    }

    // True when the first bytes of the saved file match its extension.
    public static async Task<bool> SignatureMatchesAsync(string path, string ext, CancellationToken ct)
    {
        var buf = new byte[1024];
        int n;
        await using (var fs = File.OpenRead(path))
            n = await fs.ReadAsync(buf.AsMemory(0, buf.Length), ct);

        return ext switch
        {
            ".pdf"  => Encoding.Latin1.GetString(buf, 0, n).Contains("%PDF-"),
            ".docx" => n >= 4 && buf[0] == 0x50 && buf[1] == 0x4B,   // ZIP container ("PK")
            _       => false
        };
    }
}

