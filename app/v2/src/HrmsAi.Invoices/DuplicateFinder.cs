using System.Globalization;
using Dapper;
using HrmsAi.Abstractions;

namespace HrmsAi.Invoices;

public class DuplicateRow
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Has this client already sent an invoice with the same number and total?
public class DuplicateFinder(IDbConnectionFactory db, ILogger<DuplicateFinder> log)
{
    public async Task<DuplicateInfo?> FindAsync(ParsedInvoice p, AiJob job, CancellationToken ct)
    {
        if (job.ApiKeyId is null || string.IsNullOrWhiteSpace(p.Invoice.Number) || p.Totals.Total is null) return null;
        try
        {
            using var c = db.Create();
            var row = await c.QueryFirstOrDefaultAsync<DuplicateRow>(
                @"SELECT TOP (1) Id, CreatedAt FROM dbo.AiJobs
                  WHERE JobType = 'invoice' AND Status = 'Done' AND Id <> @id AND ApiKeyId = @keyId
                    AND JSON_VALUE(ResultJson, '$.invoice.number') = @number
                    AND JSON_VALUE(ResultJson, '$.totals.total') = @total
                  ORDER BY CreatedAt DESC",
                new
                {
                    id = job.Id,
                    keyId = job.ApiKeyId,
                    number = p.Invoice.Number,
                    total = p.Totals.Total.Value.ToString("0.00", CultureInfo.InvariantCulture)
                });
            return row is null ? null : new DuplicateInfo { RequestId = "job:" + row.Id, At = row.CreatedAt };
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "The duplicate check was skipped");
            return null;
        }
    }
}
