using HrmsAi.Resume.Settings;
using Microsoft.Extensions.Options;

namespace HrmsAi.Resume.Services;

// The only place that touches the resume folder on disk
public class ResumeFileStorage(IOptions<StorageSettings> options)
{
    private readonly string _folder = options.Value.ResumesPath;

    public async Task<string> SaveAsync(IFormFile file, Guid id, string ext, CancellationToken ct)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, id + ext);
        await using var fs = File.Create(path);
        await file.CopyToAsync(fs, ct);
        return path;
    }

    public void Delete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}

