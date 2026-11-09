using System.Text.Json;
using HrmsAi.Abstractions;
using Microsoft.Extensions.Options;

namespace HrmsAi.Invoices;

// Runs "invoice" jobs for the platform's job worker
public class InvoiceJobHandler(
    InvoiceReader reader,
    InvoiceExtractor extractor,
    DuplicateFinder duplicates,
    IOptions<InvoiceSettings> options,
    IOptions<AiOptions> ai) : IJobHandler
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Module => "invoice";
    public string JobType => "invoice";

    public async Task<JobOutcome> HandleAsync(AiJob job, CancellationToken ct)
    {
        var s = options.Value;
        var input = JsonSerializer.Deserialize<InvoiceJobInput>(job.InputText ?? "", Web)
                    ?? throw new InvalidOperationException("The invoice job has no input.");
        try
        {
            if (!File.Exists(input.FilePath))
                throw new InvalidOperationException("The uploaded file is no longer available. Please upload it again.");

            var (text, source, pages) = await reader.ReadAsync(input.FilePath, input.Kind, ct);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("No readable text was found in the document.");

            var (data, usage) = await extractor.ExtractAsync(text, input.IncludeLineItems, ct);
            var parsed = InvoiceNormalizer.Normalize(data, text, source, pages, s, s.Model ?? ai.Value.ChatModel);

            var duplicate = await duplicates.FindAsync(parsed, job, ct);
            if (duplicate is not null)
            {
                parsed.PossibleDuplicate = duplicate;
                parsed.Warnings.Add($"An invoice with the same number and total was already sent on {duplicate.At:yyyy-MM-dd} (request {duplicate.RequestId}).");
            }
            return new JobOutcome(JsonSerializer.Serialize(parsed, Web), usage);
        }
        finally
        {
            if (!s.KeepFiles) { try { File.Delete(input.FilePath); } catch { /* best effort */ } }
        }
    }
}
