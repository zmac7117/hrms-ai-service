# HRMS AI Service

A small, self-hosted AI API for an HRMS application. It runs on one Windows server **without a GPU**, keeps all data on that server, and is built from separate modules, so a client who only needs resume parsing gets a build with no chat code or configuration.

| Feature | Status |
|---|---|
| Resume parsing (PDF and DOCX to one fixed JSON contract) | Working |
| Several API keys, per-key token limits, deactivation, usage tracking | Working |
| Admin web page for the keys (`/admin`, reachable only from the server itself) | Working |
| Background job queue with fair scheduling, queue limits and time estimate | Working |
| Control panel (status, start and stop of every component) | Working |
| Resume-to-job match score | Planned |
| Job description generator | Planned |
| Support chatbot (the module exists as a skeleton) | Planned |
| Invoice parser with OCR | Planned |

## Stack
.NET 10 (ASP.NET Core) on IIS, SQL Server, [Ollama](https://ollama.com) running `qwen2.5:3b` on the CPU, NSSM to run Ollama and the tunnel as Windows services, ngrok (optional) for public access. Tested on a PC with 8 CPU cores and 30 GB RAM. A resume takes about 30 seconds.

## Architecture
```text
HRMS application (its backend)
      |  HTTPS + X-API-Key header
      v
Tunnel (ngrok, Cloudflare Tunnel) or your internal network
      v
IIS site "HRMSAI-Api"  (ASP.NET Core)
   |- Access    API keys, token limits, usage, /admin page
   |- Resume    upload, text extraction, AI parsing, normalizing
   |- Chat      (skeleton)
   |- Platform  job queue and worker, health and jobs endpoints
   |- SQL Server   database "HrmsAi"
   |- Ollama service   127.0.0.1:11434   (never exposed outside the server)
```
Details are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Repository layout
```text
app/v2/src/
  HrmsAi.Abstractions   contracts only (modules, job handlers, quota and usage hooks)
  HrmsAi.Platform       shared runtime: Ollama client, SQL job queue, worker, middleware, health
  HrmsAi.Access         API keys, quotas, usage tracking, admin UI
  HrmsAi.Resume         resume domain: PDF/DOCX extraction, parser, normalizer, endpoints
  HrmsAi.Chat           chat domain (skeleton)
  HrmsAi.Host.Resume    build for clients that need resume parsing only (Access + Resume)
  HrmsAi.Host.Full      build with everything (Access + Resume + Chat)
scripts/                start, stop, status, publish, control panel, benchmark
sql/                    database scripts, run in order
docs/                   API, deployment, operations, architecture
```

## Build variants
| Host project | Contains | Use for |
|---|---|---|
| `HrmsAi.Host.Resume` | Access + Resume | Clients that only need resume parsing. No chat code in the output |
| `HrmsAi.Host.Full` | Access + Resume + Chat | Everything |

`GET /api/health` lists the modules of the running build.

## Quick start (development)
1. Install the .NET 10 SDK, SQL Server (Express is fine) with SSMS, and Ollama. Pull the model: `ollama pull qwen2.5:3b`.
2. In SSMS, run the files in `sql/` in order (001 to 004). Edit `004` if your IIS site is not named `HRMSAI-Api`, or skip it for local development.
3. In the host folder, copy `appsettings.Development.json.example` to `appsettings.Development.json` and set your own key and admin password. This file is ignored by Git.
4. Run a host, for example:
   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = "Development"
   dotnet run --project app/v2/src/HrmsAi.Host.Full --urls http://localhost:5200
   ```
   In Visual Studio, use the `http` launch profile (resume host on port 5100, full host on 5200).
5. Open `http://localhost:5200/admin`, sign in with the admin password, and create an API key.
6. Call the API with that key (see [docs/API.md](docs/API.md)).

## Configuration
| Setting | Meaning | Default |
|---|---|---|
| `ConnectionStrings:HrmsAi` | SQL Server connection (Windows authentication) | none |
| `Ai:BaseUrl` | Ollama address | `http://localhost:11434` |
| `Ai:ChatModel` | Model used for parsing | `qwen2.5:3b` |
| `Ai:TimeoutSeconds` | Timeout of one AI call | 300 |
| `Storage:ResumesPath` | Where uploaded files are kept | `C:\HRMS AI\data\resumes` |
| `Storage:MaxFileSizeMb` | Upload size limit | 10 |
| `Queue:Concurrency` | Jobs processed at the same time (match `OLLAMA_NUM_PARALLEL`) | 1 |
| `Queue:MaxPendingPerKey` | Waiting plus running jobs allowed per key (0 = no limit) | 20 |
| `Queue:MaxPendingTotal` | Waiting plus running jobs allowed in total (0 = no limit) | 200 |
| `Admin:Password` | Password of the admin page. Set it as an environment variable `Admin__Password` | none |
| `Admin:AllowRemote` | Allow the admin page from other computers. Keep `false` | false |
| `Security:ApiKey` | Old single shared key, still accepted as a fallback. Remove it once clients have their own keys | none |

Secrets are never kept in files that are committed. On the server, set them as environment variables of the IIS application pool (see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)).

## Security notes
- Resumes and invoices are personal data. Keep this repository private, and keep `data/` and the database restricted.
- API keys are stored only as hashes and shown once, when created.
- The admin page answers 404 to anything that arrives through a tunnel or proxy, and asks for a password.
- Ollama listens on `127.0.0.1` only. Only the IIS port is exposed.
- Parsing results are suggestions. Show them to HR in an editable form, and do not auto-reject candidates on AI output.

## Documentation
- [docs/API.md](docs/API.md): how an HRMS application calls the service
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md): setting up and updating the server
- [docs/OPERATIONS.md](docs/OPERATIONS.md): daily use, control panel, troubleshooting
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how it works, and how to add a module