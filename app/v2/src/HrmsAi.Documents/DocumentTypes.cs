namespace HrmsAi.Documents;

public static class DocumentTypes
{
    // Looks at the first bytes of a file. Returns "pdf", "docx", "doc", "txt", or null when unknown.
    public static string? Detect(byte[] bytes)
    {
        if (bytes.Length == 0) return null;

        var head = System.Text.Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
        if (head.Contains("%PDF-")) return "pdf";
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B) return "docx";                                  // ZIP container
        if (bytes.Length >= 4 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0) return "doc";   // old Word

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "jpg";
        if (bytes.Length >= 4 && ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
                                  (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A))) return "tiff";
        if (bytes.Length >= 30 && bytes[0] == 0x42 && bytes[1] == 0x4D &&
            (bytes[14] == 12 || bytes[14] == 40 || bytes[14] == 52 || bytes[14] == 56 || bytes[14] == 108 || bytes[14] == 124)) return "bmp";

        var n = Math.Min(bytes.Length, 2000);
        for (var i = 0; i < n; i++)
        {
            var b = bytes[i];
            if (b == 0 || b < 9 || (b > 13 && b < 32)) return null;   // binary data, not text
        }
        return "txt";
    }
}

