# HRMS AI Service

A self-hosted AI API for an HRMS application. It runs on one Windows server **without a GPU**, keeps all data on that server, and is built from separate modules, so each client gets exactly the services it needs: a client who only wants resume parsing gets a build with no other code or configuration.

## Services
| Service | Module | Route prefix | Status |
|---|---|---|---|
| Resume parsing (PDF, DOCX) to one fixed JSON contract | `resume` | `/api/resume` | Working |
| Job description generator (Groq-compatible and native) | `jd` | `/api/jd` | Available, being tested |
| Goals generator (Groq-compatible and native) | `goals` | `/api/goals` | Available, being tested |
| Resume-to-job match score (SharpAPI-compatible) | `match` | `/api/match` | Available, being tested |
| Invoice parsing with OCR, for the Expense module | `invoice` | `/api/invoice` | Available, being tested |
| Support chatbot | `chat` | `/api/chat` | Planned (skeleton only) |

Platform features: several API keys with per-client service access and token limits, an admin page for the keys, a dashboard for each client and for the admin, a background job queue with fair scheduling and limits, and a Windows control panel.

## Stack
.NET 10 (ASP.NET Core) on IIS, SQL Server, [Ollama](https://ollama.com) running `qwen2.5:3b` on the CPU, NSSM to run Ollama and the tunnel as Windows services, Tesseract and Poppler for OCR, ngrok (optional) for public access. Tested on a PC with 8 CPU cores and 30 GB RAM. Typical times: a resume 20 to 50 seconds, a job description or goals 30 to 60 seconds, an invoice 20 to 40 seconds plus a few seconds of OCR per scanned page.

## Architecture
```text
HRMS application (its backend)
      |  HTTPS + API key (X-API-Key header or Authorization: Bearer)
      v
Tunnel (ngrok, Cloudflare Tunnel) or your internal network
      v
IIS site "HRMSAI-Api"  (ASP.NET Core)
   |- Access        API keys, token limits, usage, admin page, dashboards
   |- Resume / JobDescription / Goals / ResumeMatch / Invoices / Chat   (feature modules)
   |- Platform      job queue and worker, AI slots, health, jobs endpoints
   |- SQL Server    database "HrmsAi"
   |- Ollama service   127.0.0.1:11434   (never exposed outside the server)
   |- Tesseract, Poppler   (run as separate programs for OCR)
```
Details are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Repository layout
```text
app/v2/src/
  HrmsAi.Abstractions     contracts only (modules, job handlers, quota, usage, AI slots)
  HrmsAi.Platform         shared runtime: Ollama client, SQL job queue, worker, middleware, health
  HrmsAi.Access           API keys, quotas, usage, admin page, dashboards
  HrmsAi.Compat           shared Groq / OpenAI compatible chat plumbing
  HrmsAi.Documents        PDF, DOCX and text reading, file type detection, OCR
  HrmsAi.Resume           resume domain
  HrmsAi.JobDescription   job description generator
  HrmsAi.Goals            goals generator
  HrmsAi.ResumeMatch      resume-to-job match score
  HrmsAi.Invoices         invoice parsing
  HrmsAi.Chat             chat domain (skeleton)
  HrmsAi.Host.Resume      build for clients that need resume parsing only
  HrmsAi.Host.Full        build with every module
  HrmsAi.Host.<Client>    client builds made with scripts\new-host.ps1
scripts/                  start, stop, status, publish, control panel, new-host, benchmark
sql/                      database scripts, run in order (001 to 005)
docs/                     guides
```

## Build variants
| Host project | Contains | Use for |
|---|---|---|
| `HrmsAi.Host.Resume` | Access + Resume | Clients that only need resume parsing |
| `HrmsAi.Host.Full` | Every module | Everything |
| `HrmsAi.Host.<Client>` | The modules you choose | One client: `scripts\new-host.ps1 -Name AcmeHr -Modules resume,jd` |

Besides the build, a key can be limited to some services in the admin page (module names `resume`, `jd`, `goals`, `match`, `invoice`). `GET /api/health` lists the modules of the running build.

## Quick start (development)
1. Install the .NET 10 SDK, SQL Server (Express is fine) with SSMS, and Ollama. Pull the model: `ollama pull qwen2.5:3b`.
2. In SSMS, run the files in `sql/` in order (001 to 005). Skip 004 for local development.
3. In the host folder, copy `appsettings.Development.json.example` to `appsettings.Development.json` and set your own key and admin password. This file is ignored by Git.
4. Run a host: `$env:ASPNETCORE_ENVIRONMENT = "Development"; dotnet run --project app/v2/src/HrmsAi.Host.Full --urls http://localhost:5200`. In Visual Studio use the `http` launch profile (resume host port 5100, full host 5200).
5. Open `http://localhost:5200/admin`, sign in with the admin password, create an API key, and call the API (see [docs/API.md](docs/API.md) and [docs/SERVICES.md](docs/SERVICES.md)).
6. For invoice OCR, install Tesseract and Poppler under `tools/` (see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)).

## Configuration
| Setting | Meaning | Default |
|---|---|---|
| `ConnectionStrings:HrmsAi` | SQL Server connection (Windows authentication) | none |
| `Ai:BaseUrl`, `Ai:ChatModel`, `Ai:TimeoutSeconds` | Ollama address, model, timeout of one AI call | `http://localhost:11434`, `qwen2.5:3b`, 300 |
| `Storage:ResumesPath`, `Storage:MaxFileSizeMb` | Where uploaded resumes are kept, upload limit | `C:\HRMS AI\data\resumes`, 10 |
| `Queue:Concurrency` | Jobs and direct AI calls running at the same time (match `OLLAMA_NUM_PARALLEL`) | 1 |
| `Queue:MaxPendingPerKey`, `Queue:MaxPendingTotal` | Waiting plus running jobs allowed per key and in total (0 = no limit) | 20, 200 |
| `Jd:*`, `Goals:*` | `Temperature`, `MaxOutputTokens`, `MaxWaitSeconds`, `Model` (and `Goals:WrapArrayAsObject`) | see SERVICES.md |
| `Match:*` | `MaxResumeChars`, `MaxJobChars`, `EncodeHtml`, `PublicBaseUrl`, `Model` | see SERVICES.md |
| `Invoice:*` | `DateOrder`, `DefaultCurrency`, `Categories`, `IncludeLineItems`, `KeepFiles`, `StoragePath` | see SERVICES.md |
| `Ocr:*` | `Language`, `PageSegMode`, `MaxPages`, `Dpi`, `ToolsRoot`, `TempPath` | `eng`, 6, 3, 200 |
| `Activity:StoreResults` | Keep the results of direct calls for the dashboards | true |
| `Admin:Password` | Admin page password. Set it as the environment variable `Admin__Password` | none |
| `Admin:AllowRemote` | Allow the admin pages from other computers. Keep `false` | false |
| `Security:ApiKey` | Old single shared key, still accepted as a fallback. Remove it once clients have their own keys | none |

Secrets are never kept in files that are committed. On the server, set them as environment variables of the IIS application pool (see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)).

## Security notes
- Resumes, invoices and the results are personal and financial data. Keep this repository private, and keep `data/` and the database restricted.
- API keys are stored only as hashes and shown once, when created. Each client sees only its own requests.
- The admin pages answer 404 to anything that arrives through a tunnel or proxy, and ask for a password.
- Ollama listens on `127.0.0.1` only. Only the IIS port is exposed.
- AI results are suggestions. Show them to people in an editable form and do not act automatically on them.

## Documentation
- [docs/API.md](docs/API.md): how an HRMS application calls the service (keys, resume parsing, jobs)
- [docs/SERVICES.md](docs/SERVICES.md): job description, goals, match score, invoice parsing
- [docs/DASHBOARD.md](docs/DASHBOARD.md): the client dashboard and the admin activity page
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md): setting up and updating the server
- [docs/OPERATIONS.md](docs/OPERATIONS.md): daily use, control panel, troubleshooting
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how it works, and how to add a module
- [CHANGELOG.md](CHANGELOG.md)