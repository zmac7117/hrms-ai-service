# Architecture

## Modules
The system is a modular monolith. A **module** (`IModule`) registers its own services and endpoints and can own job types. A **host** project lists the modules it wants, in about 10 lines:
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddHrmsAi(new AccessModule(), new ResumeModule());
var app = builder.Build();
app.UseHrmsAi();
app.Run();
```
`HrmsAi.Host.Resume` does not reference `HrmsAi.Chat`, so chat code is not in that build at all.

| Project | Responsibility | Depends on |
|---|---|---|
| Abstractions | Contracts: `IModule`, `IJobHandler`, `IJobStore`, `IApiKeyValidator`, `IQuotaGate`, `IUsageRecorder`, `AiUsage` | none |
| Platform | Ollama client, SQL job store, job worker, API key middleware, error handling, health and jobs endpoints | Abstractions |
| Access | Database-backed API keys, quotas, usage, admin UI | Abstractions |
| Resume | Extraction (PDF, DOCX), parser, normalizer, upload flow, endpoints, job handler | Abstractions |
| Chat | Skeleton | Abstractions |

## Request flow (resume upload)
1. `ApiKeyMiddleware` asks the `IApiKeyValidator` (Access) to check the key, its status, expiry and the module it may use.
2. The endpoint filter `RequireQuota()` asks the `IQuotaGate` whether the key still has tokens left (429 if not).
3. `ResumeUploadService` validates the file (type, size, content signature), saves it, extracts the text, and queues a `resume` job with the key's id. The extracted text is stored **in the job row**.
4. The `JobWorker` claims the next job and gives it to the module that owns the job type (`ResumeJobHandler`). The handler calls the AI and returns the result and the token usage.
5. The worker stores the result and records the usage (`IUsageRecorder`). The client polls `GET /api/jobs/{id}`.

## Job queue
- Table `AiJobs` in SQL Server. A job moves `Queued`, `Running`, `Done` or `Failed`.
- A worker only claims job types it has a handler for, and claims with a conditional `UPDATE`, so several workers (even on several machines) can share one queue.
- **Fair order:** every key's first waiting job, then every key's second, and so on, so one client's bulk upload cannot block the others.
- Limits: waiting plus running jobs per key and in total (`Queue:*`). When exceeded, 429 or 503.
- Jobs left `Running` after a restart go back to `Queued`.
- `Queue:Concurrency` controls how many jobs run at once. It only helps together with a matching `OLLAMA_NUM_PARALLEL`, and should be tested with the benchmark script.

## Access control
- Keys look like `hrms_` plus random characters. Only a SHA-256 hash and a short prefix are stored. The full key is shown once.
- Each key has: client name, active flag, optional expiry date, allowed modules, token limit (or unlimited) per day, month or in total.
- Validated keys are cached for 30 seconds. Any admin change clears the cache in the running process.
- Usage rows (prompt, completion and total tokens) are written after each AI call. The limit is checked before a request starts, so a key can pass its limit by at most one job.
- A key only sees its own jobs. The old single key from configuration is still accepted as a fallback (unlimited, not metered, sees all jobs) until it is removed.
- The admin page and API are served only when the request comes from the server itself (no forwarding headers) and with the admin password.

## Resume output contract
The AI fills a simple extraction model. Plain code (`ResumeNormalizer`) then builds the fixed public model (`ParsedResume`, schema 1.0): dates become structured, email and phone are checked against the resume text, skills are split and de-duplicated, total experience is computed from the job dates (overlaps counted once) when the resume does not state it, and `warnings` lists anything to double check. The shape therefore never depends on what the AI returns.

## Adding a module
1. Create a class library `HrmsAi.<Name>` that references `HrmsAi.Abstractions`.
2. Implement `IModule` (name, route prefix, services, endpoints). If it does background work, implement `IJobHandler` for its job type and register it.
3. Put `.RequireQuota()` on endpoints that use AI tokens.
4. Add the module to the `Program.cs` of the hosts that should include it.

## Performance
See "Capacity notes" in [OPERATIONS.md](OPERATIONS.md). Measured on 8 cores and no GPU: reading the prompt runs at about 196 tokens per second, writing at about 14, so the length of the **answer** decides the time. The result for the same resume varies by about 1.5 seconds.