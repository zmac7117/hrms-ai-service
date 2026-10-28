using HrmsAi.Resume.Models;
using HrmsAi.Resume.Settings;
using HrmsAi.Abstractions;
using Microsoft.Extensions.Options;

namespace HrmsAi.Resume.Services;

// Upload flow: validate -> save -> check content -> extract text -> queue a job
public class ResumeUploadService(
    ResumeFileStorage storage,
    IJobStore jobs,
    IOptions<StorageSettings> settings,
    ILogger<ResumeUploadService> log)
{
    public async Task<UploadResult> SubmitAsync(IFormFile file, Guid? apiKeyId, CancellationToken ct)
    {
        var maxBytes = settings.Value.MaxFileSizeMb * 1024L * 1024L;
        var problem = UploadValidator.ValidateBasics(file, maxBytes, out var ext);
        if (problem is not null)
            return UploadResult.Fail(StatusCodes.Status400BadRequest, problem);

        var id = Guid.NewGuid();
        var path = await storage.SaveAsync(file, id, ext, ct);

        if (!await UploadValidator.SignatureMatchesAsync(path, ext, ct))
        {
            storage.Delete(path);
            return UploadResult.Fail(StatusCodes.Status422UnprocessableEntity,
                $"The file is not a valid {ext.TrimStart('.').ToUpperInvariant()} (its content does not match its extension).");
        }

        string text;
        try
        {
            await using var fs = File.OpenRead(path);
            text = DocumentTextExtractor.Extract(fs, ext);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Could not read uploaded file {Name}", file.FileName);
            storage.Delete(path);
            return UploadResult.Fail(StatusCodes.Status422UnprocessableEntity,
                "The file could not be read. It may be corrupted or password protected. Please re-save or re-export it and try again.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            storage.Delete(path);
            return UploadResult.Fail(StatusCodes.Status422UnprocessableEntity,
                "No text found. This looks like a scanned file and needs OCR (a later stage).");
        }

        try { await jobs.CreateAsync(id, "resume", Path.GetFileName(file.FileName), text, apiKeyId); } catch (QueueFullException qex) { storage.Delete(path); return UploadResult.Fail(qex.StatusCode, qex.Message); }
        return UploadResult.Accepted(id);
    }
}






