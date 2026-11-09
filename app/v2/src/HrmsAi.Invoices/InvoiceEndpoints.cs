using HrmsAi.Abstractions;
using HrmsAi.Documents;

namespace HrmsAi.Invoices;

public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoice");
        group.MapPost("/parse-file", ParseFile).DisableAntiforgery().RequireQuota();   // returns a job id; poll /api/jobs/{id}
        group.MapGet("/ocr-check", OcrCheck);                                           // are the OCR tools installed?
        return app;
    }

    private static async Task<IResult> ParseFile(HttpContext ctx, InvoiceSubmitService svc, CancellationToken ct)
    {
        var r = await svc.SubmitAsync(ctx, ct);
        return r.Ok
            ? Results.Accepted($"/api/jobs/{r.JobId}", new { jobId = r.JobId, status = "Queued" })
            : Results.Json(new { error = r.Error }, statusCode: r.StatusCode);
    }

    private static async Task<IResult> OcrCheck(OcrEngine ocr, CancellationToken ct) => Results.Ok(await ocr.CheckAsync(ct));
}
