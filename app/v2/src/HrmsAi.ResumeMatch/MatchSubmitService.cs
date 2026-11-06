using System.Text.Json;
using HrmsAi.Abstractions;
using HrmsAi.Documents;

namespace HrmsAi.ResumeMatch;

// Reads the form, checks the file, extracts the text and queues a "match" job
public class MatchSubmitService(IJobStore jobs)
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async Task<SubmitResult> SubmitAsync(HttpContext ctx, CancellationToken ct)
    {
        if (!ctx.Request.HasFormContentType)
            return Fail(422, "Send the request as multipart/form-data with the fields 'file', 'content' and 'language'.");

        var form = await ctx.Request.ReadFormAsync(ct);
        var file = form.Files["file"];
        var jobText = form["content"].ToString();
        var language = form["language"].ToString();

        if (file is null || file.Length == 0) return Fail(422, "The resume file ('file') is required.");
        if (file.Length > MaxBytes) return Fail(422, "The resume file is larger than 10 MB.");
        if (string.IsNullOrWhiteSpace(jobText)) return Fail(422, "The job description ('content') is required.");

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var kind = DocumentTypes.Detect(bytes);
        if (kind is null) return Fail(422, "The file type is not supported. Use PDF, DOCX or TXT.");
        if (kind == "doc") return Fail(422, "Old .doc files are not supported. Please save the resume as DOCX or PDF.");

        string text;
        try
        {
            using var stream = new MemoryStream(bytes);
            text = DocumentTextExtractor.Extract(stream, "." + kind);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(422, "The resume could not be read. It may be damaged or password protected.");
        }
        if (string.IsNullOrWhiteSpace(text))
            return Fail(422, "No text was found in the resume. It looks like a scanned document, which needs OCR.");

        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new MatchInput(text, jobText.Trim(), language), Web);
        await jobs.CreateAsync(id, "match", Path.GetFileName(file.FileName), payload, ctx.GetCaller()?.KeyId);
        return new SubmitResult(true, 202, null, id);
    }

    private static SubmitResult Fail(int status, string error) => new(false, status, error, Guid.Empty);
}
