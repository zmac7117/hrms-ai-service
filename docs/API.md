# API guide for the HRMS application

Base URL: `https://<your-tunnel-domain>` (or `http://<server>:8090` on the internal network).
Call the API **from your HRMS backend only**, never from browser code, because the key would be exposed.

## Authentication
Every request except `GET /api/health` needs the header `X-API-Key: <key>`. Keys are created in the admin page. Behind ngrok's free plan, also send `ngrok-skip-browser-warning: 1`, or the response is an HTML warning page.

## Endpoints
| Method and path | Purpose |
|---|---|
| `GET /api/health` | Status and loaded modules. No key needed |
| `POST /api/resume/parse-file` | Upload a PDF or DOCX (form field `file`, max 10 MB). Returns a job id |
| `POST /api/resume/parse-text` | Parse pasted text and wait for the result (about 15 to 30 seconds). For tests |
| `GET /api/jobs/{id}` | Status and result of one job |
| `GET /api/jobs?take=20` | Your latest jobs (a key only sees its own jobs) |

### Upload a resume
`POST /api/resume/parse-file` (multipart form, field `file`) returns **202**:
```json
{ "jobId": "0bb9f7ea-6592-4162-8a80-31a00c13810b", "status": "Queued" }
```
Poll `GET /api/jobs/{jobId}` every 3 to 5 seconds. `status` goes `Queued`, `Running`, then `Done` or `Failed`.
```json
{
  "id": "0bb9f7ea-...", "jobType": "resume", "status": "Queued",
  "queuePosition": 3, "estimatedSecondsRemaining": 95,
  "createdAt": "...", "startedAt": null, "completedAt": null, "durationMs": null,
  "error": null, "result": null
}
```
`queuePosition` and `estimatedSecondsRemaining` appear while waiting or running. When `status` is `Done`, `result` holds the resume object. When `Failed`, `error` says why.

Supported files: PDF with selectable text, and DOCX. Old `.doc` files are refused (ask the user to save as DOCX or PDF). Scanned PDFs are refused until OCR is added.

### Resume result (schema version 1.0)
Every field is always present. Unknown single values are `null`, lists are `[]`.
```json
{
  "schemaVersion": "1.0",
  "candidate": { "fullName": "...", "email": "...", "phone": "..." },
  "totalExperienceYears": 8.0,
  "experienceYearsSource": "calculated",
  "skills": ["C#", ".NET", "SQL Server"],
  "experience": [
    { "company": "...", "title": "...",
      "start": { "text": "April 2020", "year": 2020, "month": 4, "isCurrent": false },
      "end":   { "text": "Present",    "year": null, "month": null, "isCurrent": true },
      "durationMonths": 79 }
  ],
  "education": [ { "degree": "...", "institution": "...", "year": 2016, "period": "September 2015 - September 2016" } ],
  "warnings": [],
  "meta": { "model": "qwen2.5:3b", "parsedAtUtc": "2026-10-15T10:30:29Z" }
}
```
Rules: email and phone must exist in the resume text (the code replaces invented ones). `experienceYearsSource` is `stated`, `calculated` or `unknown`. `warnings` lists things HR should double check. New optional fields may be added in later 1.x versions, so ignore fields you do not know.

## Errors
| Code | Meaning |
|---|---|
| 400 | Bad request, or an unsupported file type or size |
| 401 | Missing or unknown API key |
| 403 | The key is deactivated, expired, or not allowed to use this module (the `error` text says which) |
| 404 | Unknown job, or the job belongs to another key |
| 422 | The file is damaged, password protected, has no text, or its content does not match its extension |
| 429 | The token limit of this key is reached, or too many jobs of this key are waiting |
| 503 | The service queue is full. Try again in a few minutes |
| 500 | Unexpected error. The body is `{ "error": "..." }` |

All errors are JSON: `{ "error": "message" }`.

## Recommended flow in the HRMS
1. The user uploads a resume. The backend sends it and stores the `jobId` with the candidate (status "Parsing").
2. Poll the job. Show "position 3, about 1 minute" while it waits, using the two queue fields.
3. On `Done`, pre-fill an **editable** form and show `warnings`. HR corrects and saves. Keep the original AI value next to the correction, because it is useful for future training.
4. On `Failed`, or on 422, offer manual entry.
5. Handle 429 and 503 by telling the user to try again shortly. Submit bulk uploads a few at a time.
6. The HRMS must keep working when this service is down.

## Example client (C#)
```csharp
public record JobAccepted(Guid JobId, string Status);
public record JobStatus(Guid Id, string Status, string? Error, int? QueuePosition,
                        int? EstimatedSecondsRemaining, System.Text.Json.JsonElement? Result);

public class HrmsAiClient(HttpClient http)
{
    public async Task<Guid> SubmitResumeAsync(Stream file, string fileName, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(file), "file", fileName);
        var resp = await http.PostAsync("/api/resume/parse-file", form, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JobAccepted>(ct))!.JobId;
    }

    public Task<JobStatus?> GetJobAsync(Guid id, CancellationToken ct)
        => http.GetFromJsonAsync<JobStatus>($"/api/jobs/{id}", ct);
}

// registration
services.AddHttpClient<HrmsAiClient>(c =>
{
    c.BaseAddress = new Uri(config["HrmsAi:BaseUrl"]!);
    c.DefaultRequestHeaders.Add("X-API-Key", config["HrmsAi:ApiKey"]);
    c.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "1");
    c.Timeout = TimeSpan.FromSeconds(60);
});
```