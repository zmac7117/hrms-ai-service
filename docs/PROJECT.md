# HRMS AI — Project Reference

> This document is written for an AI assistant (Claude) to understand the full structure of the **v2** application before making any changes. Read this before touching any file.

---

## What This Project Is

HRMS AI is a **.NET 10 Web API** that provides AI-powered HR automation features. It wraps a **local Ollama LLM** (`qwen2.5:3b`) behind a structured job queue and exposes the results through a clean REST API. All heavy AI operations are async — the client submits a job, gets back a GUID, and polls for the result.

**Active codebase:** `app/v2/` — this is the production application.  
**Legacy codebase:** `app/HrmsAi.Api/` — the original v1 prototype, kept for reference but not in active use.

---

## Runtime Dependencies

| Dependency | Detail |
|---|---|
| **SQL Server** | `Server=DESKTOP-IC78E3Q\SQL2022D;Database=HrmsAi` |
| **Ollama** | `http://localhost:11434`, model `qwen2.5:3b`, 300s timeout |
| **File storage** | `C:\HRMS AI\data\resumes` (resume uploads) |
| **Tesseract OCR** | Local installation, language `eng`, `PageSegMode=6`, max 3 pages |

---

## Solution Layout

```
app/v2/
├── HrmsAi.slnx                    ← Solution file
└── src/
    ├── HrmsAi.Abstractions/       ← Shared contracts only (no implementations)
    ├── HrmsAi.Platform/           ← Shared runtime (bootstrap, DB, jobs, middleware)
    ├── HrmsAi.Access/             ← API key management, quotas, usage tracking, admin UI
    ├── HrmsAi.Resume/             ← Resume upload, extraction, AI parsing
    ├── HrmsAi.JobDescription/     ← AI job description generation
    ├── HrmsAi.Goals/              ← AI HR goal/objective generation
    ├── HrmsAi.ResumeMatch/        ← AI resume-to-job scoring
    ├── HrmsAi.Invoices/           ← Invoice OCR + AI extraction
    ├── HrmsAi.Chat/               ← Chat module (stub — not implemented yet)
    ├── HrmsAi.Documents/          ← Shared document text extraction library
    ├── HrmsAi.Compat/             ← OpenAI-compatible API wrapper
    ├── HrmsAi.Host.Full/          ← Deployable host: all 7 modules
    └── HrmsAi.Host.Resume/        ← Slim deployable host: Access + Resume only
```

---

## Core Architecture Pattern — The Module System

Every feature is an `IModule`. The host selects which modules to load at startup.

### `IModule` contract (`HrmsAi.Abstractions/IModule.cs`)
```csharp
public interface IModule
{
    string Name { get; }                              // e.g. "resume"
    string RoutePrefix { get; }                       // e.g. "/api/resume"
    IReadOnlyList<string> OpenPaths => Array.Empty<string>();  // skip API key check
    void ConfigureServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(IEndpointRouteBuilder app);
}
```

### Bootstrap (`HrmsAi.Platform/HrmsAiHost.cs`)
```csharp
// In Program.cs:
builder.AddHrmsAi(new AccessModule(), new ResumeModule(), ...);
app.UseHrmsAi();
```
- `AddHrmsAi(modules)` — registers shared platform services, then calls `module.ConfigureServices()` for each.
- `UseHrmsAi()` — wires middleware (`ErrorHandling → ApiKey`), maps shared endpoints, then calls `module.MapEndpoints()` for each.

### Full host entry point (`HrmsAi.Host.Full/Program.cs`)
```csharp
builder.AddHrmsAi(
    new AccessModule(),
    new ResumeModule(),
    new JobDescriptionModule(),
    new GoalsModule(),
    new MatchModule(),
    new InvoiceModule(),
    new ChatModule()
);
app.UseHrmsAi();
app.Run();
```

---

## Async Job Queue

Every AI-intensive operation goes through the queue. No blocking synchronous AI calls.

**Flow:**
1. Client `POST`s a request → endpoint calls `IJobStore.CreateAsync(...)` → returns job `Guid` immediately.
2. `JobWorker` (BackgroundService) polls `IJobStore.ClaimNextAsync(jobTypes[])` to atomically pick the next job.
3. Worker acquires an AI concurrency slot from `IAiGate.AcquireAsync()` to prevent CPU overload.
4. Job is dispatched to the matching `IJobHandler` by `JobType` string.
5. On success: `IJobStore.CompleteAsync(id, resultJson, durationMs)`.
6. On failure: `IJobStore.FailAsync(id, error)`.
7. Client polls `GET /api/jobs/{id}` or `GET /api/jobs` to retrieve the result.

**Concurrency** is controlled by `Queue:Concurrency` in config (default: 1, max 8).

---

## Security Model — Two-Phase Design

| Phase | When Active | Implementation |
|---|---|---|
| Phase 1 (defaults) | No `AccessModule` loaded | `ConfigApiKeyValidator` (config-file key), `AllowAllQuotaGate`, `NoOpUsageRecorder` |
| Phase 2 (full) | `AccessModule` loaded | `DbApiKeyValidator`, `DbQuotaGate`, `ActivityUsageRecorder` — swapped in via `services.Replace(...)` |

**API key middleware** (`ApiKeyMiddleware.cs`) validates the `X-Api-Key` header on every request except paths in `IModule.OpenPaths`. The `/admin` and `/dashboard` paths are open (admin has its own password).

**Quota enforcement** is applied at the endpoint level by calling `.RequireQuota()` on a route, which attaches `QuotaEndpointFilter`.

---

## Project Details

---

### `HrmsAi.Abstractions`
Pure contracts. No implementations. Every other project references this.

| File | Purpose |
|---|---|
| `IModule.cs` | Core module plugin contract |
| `IAiGate.cs` | AI concurrency slot interface — `AcquireAsync(maxWait, ct)` returns `IDisposable?` (null = timed out) |
| `IDbConnectionFactory.cs` | Factory for `IDbConnection` |
| `Jobs.cs` | `IJobStore`, `IJobHandler`, `JobOutcome` |
| `AiJob.cs` | Job entity: Id, JobType, Status, InputFileName, InputText, ResultJson, Error, ApiKeyId, timestamps, DurationMs |
| `Access.cs` | `IApiKeyValidator`, `IQuotaGate`, `CallerInfo`, `AuthResult`, `QuotaResult` |
| `Usage.cs` | `IUsageRecorder`, `AiUsage`, `UsageRecord` |
| `AiOptions.cs` | Config model: BaseUrl, ChatModel, TimeoutSeconds |
| `CallerExtensions.cs` | `HttpContext.GetCaller()` — extracts `CallerInfo` from request |
| `QuotaEndpointFilter.cs` | `IEndpointFilter` + `.RequireQuota()` extension for route builders |
| `QueueFullException.cs` | Thrown when job queue limits are exceeded |

---

### `HrmsAi.Platform`
Shared runtime. Registered by `HrmsAiHost.AddHrmsAi()`. All feature modules depend on this.

```
HrmsAi.Platform/
├── HrmsAiHost.cs              ← AddHrmsAi() / UseHrmsAi() extension methods
├── Data/
│   ├── SqlConnectionFactory.cs   ← IDbConnectionFactory → SQL Server via SqlClient
│   └── SqlJobStore.cs            ← IJobStore backed by SQL Server (Dapper queries)
├── Jobs/
│   ├── JobWorker.cs              ← BackgroundService: poll → claim → dispatch → record
│   ├── AiGate.cs                 ← IAiGate: semaphore limiting concurrent AI calls
│   └── QueueSettings.cs          ← Config: Concurrency, MaxPendingPerKey, MaxPendingTotal
├── Endpoints/
│   ├── HealthEndpoints.cs        ← GET /health
│   └── JobEndpoints.cs           ← GET /api/jobs, GET /api/jobs/{id}
├── Middleware/
│   ├── ApiKeyMiddleware.cs       ← Validates X-Api-Key header, skips OpenPaths
│   └── ErrorHandlingMiddleware.cs ← Global exception → JSON error response
├── Mapping/
│   └── JobMapper.cs              ← AiJob → JobDto
├── Models/
│   └── JobDto.cs                 ← Response shape for job status polling
├── Security/
│   ├── ConfigApiKeyValidator.cs  ← Phase 1 validator (reads key from appsettings)
│   └── SecuritySettings.cs      ← Config model for Phase 1 key
└── Defaults/
    └── Defaults.cs               ← AllowAllQuotaGate, NoOpUsageRecorder (Phase 1 no-ops)
```

**NuGet packages:** `OllamaSharp 5.5.0`, `Microsoft.Extensions.AI 10.11.0`, `Dapper 2.1.89`, `Microsoft.Data.SqlClient 7.1.1`

---

### `HrmsAi.Access`
Replaces Phase 1 security defaults. Adds admin UI and usage activity tracking.

```
HrmsAi.Access/
├── AccessModule.cs                ← IModule: Name="access", RoutePrefix="/admin"
│                                     OpenPaths = ["/admin", "/dashboard"]
│                                     Replaces IApiKeyValidator, IQuotaGate, IUsageRecorder
├── Data/
│   └── ApiKeyRepository.cs        ← CRUD for API keys in SQL
├── Services/
│   ├── ApiKeyService.cs           ← Business logic: create/revoke/list keys
│   ├── DbApiKeyValidator.cs       ← IApiKeyValidator: validates key against DB, respects quotas
│   ├── DbQuotaGate.cs             ← IQuotaGate: enforces per-key token/request limits from DB
│   ├── DbUsageRecorder.cs         ← IUsageRecorder: writes usage records to activity table
│   ├── KeyCache.cs                ← In-memory cache for validated keys (IMemoryCache)
│   └── KeyGenerator.cs            ← Generates new API key strings
├── Admin/
│   ├── AdminEndpoints.cs          ← GET/POST /admin/* — key management REST endpoints
│   └── AdminGuards.cs             ← Password check for admin area
├── Activity/
│   ├── ActivityEndpoints.cs       ← GET /admin/activity — usage log endpoints
│   ├── ActivityModels.cs          ← Activity query/response DTOs
│   ├── ActivityRepository.cs      ← SQL queries for activity log
│   └── ActivityUsageRecorder.cs   ← IUsageRecorder implementation writing to activity table
├── Models/
│   ├── ApiKeyModels.cs            ← ApiKey entity (Id, Name, KeyHash, Quota, etc.)
│   └── ApiKeyDtos.cs              ← Request/response shapes
├── Settings/
│   └── AdminSettings.cs           ← Config: admin password, section name
└── Ui/
    ├── admin.html                 ← Embedded single-page admin UI (key management)
    └── activity.html              ← Embedded single-page activity/usage dashboard
```

---

### `HrmsAi.Resume`
Resume ingestion pipeline: upload → extract text → AI parse → structured data.

```
HrmsAi.Resume/
├── ResumeModule.cs               ← IModule: Name="resume", RoutePrefix="/api/resume"
├── ResumeJobHandler.cs           ← IJobHandler for JobType="resume.parse"
├── Endpoints/
│   └── ResumeEndpoints.cs        ← POST /api/resume/upload, GET /api/resume/{jobId}
├── Services/
│   ├── ResumeUploadService.cs    ← Orchestrates: validate → store → create job
│   ├── ResumeFileStorage.cs      ← Saves/reads files from StorageSettings.ResumesPath
│   ├── ResumeParserService.cs    ← Calls IChatClient with structured prompt, parses JSON response
│   ├── ResumeNormalizer.cs       ← Cleans and normalizes parsed resume fields
│   ├── ContactCleaner.cs         ← Strips/formats contact info (phone, email, LinkedIn)
│   └── UploadValidator.cs        ← Validates file type (PDF/DOCX) and size limits
├── Models/
│   ├── ResumeData.cs             ← Full structured resume (name, contact, experience, skills, education, etc.)
│   ├── ParsedResume.cs           ← Raw AI response wrapper before normalization
│   ├── ParseOutcome.cs           ← Result of a parse attempt (success/failure + data)
│   ├── ParseTextRequest.cs       ← Request to parse raw text (no file upload)
│   └── UploadResult.cs           ← Response from upload endpoint (jobId)
└── Settings/
    └── StorageSettings.cs        ← Config: ResumesPath
```

---

### `HrmsAi.JobDescription`
Generates structured job descriptions from a prompt.

```
HrmsAi.JobDescription/
├── JobDescriptionModule.cs       ← IModule: Name="jd", RoutePrefix="/api/jd"
│                                    Uses HrmsAi.Compat (AddHrmsLlm)
├── JdEndpoints.cs                ← POST /api/jd/generate (direct, not async job)
├── JdPrompt.cs                   ← Builds the LLM prompt for JD generation
├── JdModels.cs                   ← Request/response models + JdSettings config
└── HtmlCleaner.cs                ← Strips HTML tags from LLM output if present
```

Config section: `Jd` → `Temperature`, `MaxOutputTokens`, `MaxWaitSeconds`

---

### `HrmsAi.Goals`
Generates HR goals/objectives using the LLM.

```
HrmsAi.Goals/
├── GoalsModule.cs                ← IModule: Name="goals", RoutePrefix="/api/goals"
│                                    Uses HrmsAi.Compat (AddHrmsLlm)
├── GoalsEndpoints.cs             ← POST /api/goals/generate (direct, not async job)
├── GoalsPrompt.cs                ← Builds the LLM prompt
├── GoalsJson.cs                  ← Parses/validates JSON array from LLM response
└── GoalsModels.cs                ← Request/response models + GoalsSettings config
```

Config section: `Goals` → `Temperature`, `MaxOutputTokens`, `MaxWaitSeconds`, `WrapArrayAsObject`, `ObjectKey`

---

### `HrmsAi.ResumeMatch`
Scores how well a resume matches a job description using the LLM.

```
HrmsAi.ResumeMatch/
├── MatchModule.cs                ← IModule: Name="match", RoutePrefix="/api/match"
├── MatchJobHandler.cs            ← IJobHandler for JobType="match.score"
├── MatchSubmitService.cs         ← Validates inputs, creates the async job
├── MatchScorer.cs                ← Calls IChatClient with match prompt, parses score JSON
├── MatchPrompt.cs                ← Builds the scoring prompt (resume + JD → score + reasoning)
└── MatchModels.cs                ← Request/response models + MatchSettings config
```

Config section: `Match` → `Temperature`, `MaxOutputTokens`, `MaxResumeChars`, `MaxJobChars`, `EncodeHtml`

---

### `HrmsAi.Invoices`
Extracts structured data from invoice files using OCR and the LLM.

```
HrmsAi.Invoices/
├── InvoiceModule.cs              ← IModule: Name="invoice", RoutePrefix="/api/invoice"
│                                    Uses HrmsAi.Documents (AddHrmsOcr)
├── InvoiceJobHandler.cs          ← IJobHandler for JobType="invoice.extract"
├── InvoiceSubmitService.cs       ← Validates upload, stores file, creates job
├── InvoiceReader.cs              ← Reads invoice file → raw text (via HrmsAi.Documents)
├── InvoiceExtractor.cs           ← Calls IChatClient with invoice prompt, parses result
├── InvoiceNormalizer.cs          ← Normalizes dates, amounts, vendor names
├── InvoiceParsing.cs             ← JSON parsing helpers for LLM invoice output
├── DuplicateFinder.cs            ← Detects likely duplicate invoices by invoice number/amount
├── InvoicePrompt.cs              ← Builds the extraction prompt
├── ParsedInvoice.cs              ← Structured invoice entity (vendor, date, total, line items)
└── InvoiceModels.cs              ← Request/response DTOs + InvoiceSettings config
```

Config section: `Invoice` → `DateOrder`, `IncludeLineItems`, `KeepFiles`

---

### `HrmsAi.Documents`
Shared document text extraction library used by Resume and Invoices modules.

```
HrmsAi.Documents/
├── DocumentTextExtractor.cs      ← Dispatcher: routes by file extension → correct extractor
├── PdfTextExtractor.cs           ← Extracts text from PDF using PdfPig
├── PdfLayoutExtractor.cs         ← Layout-aware PDF extraction (preserves columns/tables)
├── OcrEngine.cs                  ← Tesseract OCR wrapper for scanned docs/images
├── OcrServiceExtensions.cs       ← AddHrmsOcr() DI registration for OCR
├── OcrSettings.cs                ← Config: Language, PageSegMode, MaxPages
└── DocumentTypes.cs              ← File type enum/constants
```

**NuGet packages:** `PdfPig 0.1.16`, `DocumentFormat.OpenXml 3.5.1`, Tesseract OCR  
Config section: `Ocr` → `Language`, `PageSegMode`, `MaxPages`

---

### `HrmsAi.Compat`
Wraps the internal `IChatClient` with an OpenAI-compatible API surface, so external tools (LangChain, etc.) can connect.

```
HrmsAi.Compat/
├── OpenAiCompat.cs               ← Static handler: maps OpenAI ChatCompletion request → IChatClient → OpenAI response
├── LlmExecutor.cs                ← Shared helper: runs a prompt through IChatClient with quota + usage tracking
├── CompatServiceExtensions.cs    ← AddHrmsLlm() — registers LlmExecutor
├── ChatCompletionModels.cs       ← OpenAI request/response model shapes
└── PromptSummary.cs              ← Truncates prompt text for logging
```

**Key behavior:**
- Streaming is not supported (returns 400 if `stream: true`).
- Returns 503 with `Retry-After: 30` if the AI gate is saturated.
- Maps `system`/`assistant`/`user` roles to `ChatRole` enum.

---

### `HrmsAi.Chat`
Placeholder module. Registered but not implemented.

```
HrmsAi.Chat/
├── ChatModule.cs                 ← IModule: Name="chat", RoutePrefix="/api/chat"
└── HrmsAi.Chat.csproj
```

Currently maps `GET /api/chat/status` → `501 Not Implemented`.

---

### `HrmsAi.Host.Full`
Full production host. References all 7 feature modules + Platform.

```
HrmsAi.Host.Full/
├── Program.cs                    ← Boots all modules: Access, Resume, JD, Goals, Match, Invoices, Chat
├── appsettings.json              ← SQL connection string, Ollama config, all module configs
├── appsettings.Development.json  ← Dev overrides
└── Properties/
```

---

### `HrmsAi.Host.Resume`
Slim host. Only resume processing with access control. Useful for isolated deployment.

```
HrmsAi.Host.Resume/
├── Program.cs                    ← Boots: Access, Resume
├── appsettings.json
└── appsettings.Development.json
```

---

## Configuration Reference (`appsettings.json`)

```json
{
  "ConnectionStrings": {
    "HrmsAi": "Server=...;Database=HrmsAi;..."
  },
  "Storage": {
    "ResumesPath": "C:\\HRMS AI\\data\\resumes"
  },
  "Ai": {
    "BaseUrl": "http://localhost:11434",
    "ChatModel": "qwen2.5:3b",
    "TimeoutSeconds": 300
  },
  "Queue": {
    "Concurrency": 1,
    "MaxPendingPerKey": 20,
    "MaxPendingTotal": 200
  },
  "Jd":      { "Temperature": 0.7, "MaxOutputTokens": 900, "MaxWaitSeconds": 30 },
  "Goals":   { "Temperature": 0.7, "MaxOutputTokens": 900, "MaxWaitSeconds": 30, "WrapArrayAsObject": false, "ObjectKey": "goals" },
  "Match":   { "Temperature": 0, "MaxOutputTokens": 900, "MaxResumeChars": 12000, "MaxJobChars": 6000, "EncodeHtml": true },
  "Invoice": { "DateOrder": "DMY", "IncludeLineItems": true, "KeepFiles": false },
  "Ocr":     { "Language": "eng", "PageSegMode": 6, "MaxPages": 3 }
}
```

---

## Endpoint Summary

| Method | Path | Module | Async? | Description |
|---|---|---|---|---|
| `GET` | `/health` | Platform | — | Health check |
| `GET` | `/api/jobs` | Platform | — | List recent jobs |
| `GET` | `/api/jobs/{id}` | Platform | — | Get job status/result |
| `POST` | `/api/resume/upload` | Resume | ✅ | Upload PDF/DOCX for AI parsing |
| `POST` | `/api/jd/generate` | JobDescription | ❌ | Generate a job description |
| `POST` | `/api/goals/generate` | Goals | ❌ | Generate HR goals |
| `POST` | `/api/match/score` | ResumeMatch | ✅ | Score resume vs job description |
| `POST` | `/api/invoice/upload` | Invoices | ✅ | Upload invoice for extraction |
| `GET` | `/admin/*` | Access | — | API key management (admin password) |
| `GET` | `/dashboard` | Access | — | Admin dashboard UI |
| `GET` | `/admin/activity` | Access | — | Usage activity log |
| `GET` | `/api/chat/status` | Chat | — | Returns 501 (not implemented) |

---

## Key Conventions

- **All projects target `.NET 10`** with `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.
- **No Entity Framework** — Dapper + raw SQL is used throughout for data access.
- **No controllers** — all routes use ASP.NET Core Minimal APIs.
- **Singleton services** — most services are registered as `AddSingleton`. Repositories are also singleton since Dapper handles connections per-call.
- **Config sections** — each module reads its own named config section (e.g., `config.GetSection("Jd")`).
- **IJobHandler.JobType** is a dotted string — `"resume.parse"`, `"match.score"`, `"invoice.extract"`. The `Module` property matches `IModule.Name`.
- **OpenPaths** on a module exempts those path prefixes from the API key middleware check.
- **`services.Replace(...)`** in `AccessModule` is intentional — it upgrades Phase 1 no-op security to full DB-backed security without the platform knowing about it.

---

## Project Dependency Graph

```
HrmsAi.Host.Full
├── HrmsAi.Platform
│   └── HrmsAi.Abstractions
├── HrmsAi.Access
│   └── HrmsAi.Abstractions
├── HrmsAi.Resume
│   ├── HrmsAi.Abstractions
│   └── HrmsAi.Documents
├── HrmsAi.JobDescription
│   ├── HrmsAi.Abstractions
│   └── HrmsAi.Compat
├── HrmsAi.Goals
│   ├── HrmsAi.Abstractions
│   └── HrmsAi.Compat
├── HrmsAi.ResumeMatch
│   └── HrmsAi.Abstractions
├── HrmsAi.Invoices
│   ├── HrmsAi.Abstractions
│   └── HrmsAi.Documents
└── HrmsAi.Chat
    └── HrmsAi.Abstractions

HrmsAi.Compat
└── HrmsAi.Abstractions

HrmsAi.Documents
└── (no internal deps)
```
