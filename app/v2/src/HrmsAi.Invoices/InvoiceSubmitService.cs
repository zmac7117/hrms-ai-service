using System.Text.Json;
using HrmsAi.Abstractions;
using HrmsAi.Documents;
using Microsoft.Extensions.Options;

namespace HrmsAi.Invoices;

// Reads the form, checks and saves the file, and queues an "invoice" job
public class InvoiceSubmitService(IJobStore jobs, IOptions<InvoiceSettings> options)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async Task<InvoiceSubmitResult> SubmitAsync(HttpContext ctx, CancellationToken ct)
    {
        var s = options.Value;
        if (!ctx.Request.HasFormContentType)
            return Fail(422, "Send the request as multipart/form-data with the field 'file'.");

        var form = await ctx.Request.ReadFormAsync(ct);
        var file = form.Files["file"];
        if (file is null || file.Length == 0) return Fail(422, "The file ('file') is required.");
        if (file.Length > s.MaxFileSizeMb * 1024L * 1024L) return Fail(422, $"The file is larger than {s.MaxFileSizeMb} MB.");

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var kind = DocumentTypes.Detect(bytes);
        if (kind is not ("pdf" or "png" or "jpg" or "tiff" or "bmp"))
            return Fail(422, "Send the invoice as a PDF or as an image (PNG, JPG, TIFF or BMP).");

        var includeLines = bool.TryParse(form["lineItems"].ToString(), out var asked) ? asked : s.IncludeLineItems;

        Directory.CreateDirectory(s.StoragePath);
        var id = Guid.NewGuid();
        var path = Path.Combine(s.StoragePath, id.ToString("N") + "." + kind);
        await File.WriteAllBytesAsync(path, bytes, ct);

        var payload = JsonSerializer.Serialize(new InvoiceJobInput(path, Path.GetFileName(file.FileName), kind, includeLines), Web);
        try
        {
            await jobs.CreateAsync(id, "invoice", Path.GetFileName(file.FileName), payload, ctx.GetCaller()?.KeyId);
        }
        catch
        {
            try { File.Delete(path); } catch { /* best effort */ }
            throw;
        }
        return new InvoiceSubmitResult(true, 202, null, id);
    }

    private static InvoiceSubmitResult Fail(int status, string error) => new(false, status, error, Guid.Empty);
}
