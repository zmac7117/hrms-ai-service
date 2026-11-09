using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HrmsAi.Documents;

public record OcrResult(string Text, int Pages);
public record ToolStatus(bool Found, string? Path, string? Version);
public record OcrCheck(ToolStatus Tesseract, ToolStatus Pdftoppm, string[] Languages);

// Reads text from images and scanned PDFs by running Tesseract and Poppler as separate programs.
public class OcrEngine(IOptions<OcrSettings> options, ILogger<OcrEngine> log)
{
    private string? _tesseract;
    private string? _pdftoppm;
    private OcrSettings S => options.Value;

    public async Task<OcrResult> ReadImageAsync(string imagePath, CancellationToken ct)
        => new(await RunTesseractAsync(imagePath, ct), 1);

    public async Task<OcrResult> ReadPdfAsync(string pdfPath, CancellationToken ct)
    {
        var pdftoppm = FindPdftoppm()
            ?? throw new InvalidOperationException("The PDF page renderer (pdftoppm, part of Poppler) was not found. Install it under C:\\HRMS AI\\tools\\poppler.");

        var work = Path.Combine(S.TempPath, "ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var args = new List<string>
            {
                "-r", S.Dpi.ToString(), "-f", "1", "-l", S.MaxPages.ToString(), "-png", pdfPath, Path.Combine(work, "page")
            };
            var (code, _, err) = await RunAsync(pdftoppm, args, ct);
            if (code != 0) throw new InvalidOperationException("The PDF could not be turned into images: " + Short(err));

            var pages = Directory.GetFiles(work, "page*.png").OrderBy(PageNo).ToList();
            if (pages.Count == 0) throw new InvalidOperationException("The PDF has no pages to read.");

            var sb = new StringBuilder();
            foreach (var page in pages)
            {
                sb.AppendLine(await RunTesseractAsync(page, ct));
                sb.AppendLine();
            }
            return new OcrResult(sb.ToString().Trim(), pages.Count);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* temporary folder, best effort */ }
        }
    }

    public async Task<OcrCheck> CheckAsync(CancellationToken ct)
    {
        var tess = FindTesseract();
        var ppm = FindPdftoppm();
        string? tessVersion = null, ppmVersion = null;
        var languages = Array.Empty<string>();

        if (tess is not null)
        {
            try
            {
                var (_, o, e) = await RunAsync(tess, new[] { "--version" }, ct);
                tessVersion = (o + e).Split('\n').FirstOrDefault()?.Trim();
                var (_, lo, le) = await RunAsync(tess, new[] { "--list-langs" }, ct);
                languages = (lo + le).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                     .Where(l => !l.StartsWith("List of")).ToArray();
            }
            catch (Exception ex) { tessVersion = "error: " + ex.Message; }
        }
        if (ppm is not null)
        {
            try
            {
                var (_, o, e) = await RunAsync(ppm, new[] { "-v" }, ct);
                ppmVersion = (o + e).Split('\n').FirstOrDefault()?.Trim();
            }
            catch (Exception ex) { ppmVersion = "error: " + ex.Message; }
        }
        return new OcrCheck(new ToolStatus(tess is not null, tess, tessVersion), new ToolStatus(ppm is not null, ppm, ppmVersion), languages);
    }

    // ---------- helpers ----------
    private async Task<string> RunTesseractAsync(string imagePath, CancellationToken ct)
    {
        var exe = FindTesseract()
            ?? throw new InvalidOperationException("Tesseract was not found. Install it under C:\\HRMS AI\\tools\\tesseract.");

        var args = new List<string> { imagePath, "stdout", "-l", S.Language, "--psm", S.PageSegMode.ToString(), "-c", "preserve_interword_spaces=1" };

        var tessdata = S.TessdataPath;
        if (string.IsNullOrWhiteSpace(tessdata))
        {
            var dir = Path.Combine(Path.GetDirectoryName(exe) ?? "", "tessdata");
            if (Directory.Exists(dir)) tessdata = dir;
        }
        if (!string.IsNullOrWhiteSpace(tessdata)) { args.Add("--tessdata-dir"); args.Add(tessdata); }

        var (code, stdout, err) = await RunAsync(exe, args, ct);
        if (code != 0) throw new InvalidOperationException("OCR failed: " + Short(err));
        return stdout.Trim();
    }

    private async Task<(int Code, string Out, string Err)> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(S.TimeoutSeconds));
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            log.LogWarning("{Tool} timed out after {Seconds} s", Path.GetFileName(exe), S.TimeoutSeconds);
            throw new TimeoutException($"{Path.GetFileName(exe)} did not finish within {S.TimeoutSeconds} seconds.");
        }
        return (process.ExitCode, await outTask, await errTask);
    }

    private string? FindTesseract() =>
        _tesseract ??= Resolve(S.TesseractPath, Path.Combine(S.ToolsRoot, "tesseract"), "tesseract.exe");

    private string? FindPdftoppm() =>
        _pdftoppm ??= Resolve(S.PdftoppmPath, Path.Combine(S.ToolsRoot, "poppler"), "pdftoppm.exe");

    private static string? Resolve(string? configured, string folder, string exeName)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;

        try
        {
            if (Directory.Exists(folder))
            {
                var found = Directory.EnumerateFiles(folder, exeName, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return found;
            }
        }
        catch (UnauthorizedAccessException) { /* try the PATH next */ }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static int PageNo(string file)
    {
        var m = Regex.Match(Path.GetFileName(file), @"-(\d+)\.png$");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private static string Short(string s) => s.Length <= 300 ? s.Trim() : s.Substring(0, 300).Trim();
}
