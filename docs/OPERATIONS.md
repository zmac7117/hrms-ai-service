# Operations

## Daily tools
| Tool | What it does |
|---|---|
| `scripts\HRMS-AI-Control-Panel.bat` | Window with the status of SQL Server, Ollama (and its models), the API and the tunnel, with Start, Stop and Restart on each. Asks for administrator rights |
| `scripts\start-all.bat`, `stop-all.bat`, `status.bat` | The same in console form. Stop leaves SQL Server running |
| `scripts\publish.bat` | Build and switch to a new version, with automatic rollback |
| `scripts\benchmark.ps1` | Uploads one resume several times and measures the total time (for tuning `Queue:Concurrency`) |
| `http://localhost:8090/admin` | Manage API keys (only from the server itself) |

## Health
`GET /api/health` shows the loaded modules and whether Ollama answers. A `503` from it means the API is up but Ollama is not.

## Logs
`C:\HRMS AI\logs`: `ollama.err.log`, `ngrok.err.log`, and the IIS stdout log if enabled (set `stdoutLogEnabled="true"` and `stdoutLogFile="C:\HRMS AI\logs\api-stdout"` in the release's `web.config`, then restart the site). Log files can contain tokens, so do not paste them into chats or tickets without checking.

## Troubleshooting
| Symptom | Cause and fix |
|---|---|
| Ollama service shows `Paused`, log says the address is already in use | The Ollama tray app (or another Ollama) holds port 11434. Close it (`Stop-Process -Name "ollama app","ollama"`), remove it from startup, start the service again |
| A job fails with `404 Not Found` and the model is "not found" | Another Ollama (without your models) answers on the port, or the model is missing. Check `GET http://127.0.0.1:11434/api/tags`, close the other Ollama, pull the model |
| Jobs stay `Queued` | The worker is not running (IIS site stopped, or no `AlwaysRunning`), SQL Server is unreachable, or the column `ApiKeyId` is missing (run `sql/002`). Look at the console or the stdout log |
| 401 on every call | Wrong or missing `X-API-Key`. Check for a stray space |
| 403 | Key deactivated, expired, or not allowed to use the module. See the `error` text, and the admin page |
| 429 | Token limit of the key reached, or too many waiting jobs. Change the limit in the admin page |
| `ERR_NGROK_6024` page instead of JSON | Send the header `ngrok-skip-browser-warning: 1` |
| `ERR_NGROK_3200` endpoint is offline | The tunnel service is not running. Start `HRMSAI-Tunnel` |
| Tunnel service `Paused`, log says the authtoken is invalid | The token was never set, or a placeholder was used. Redo the ngrok config step in DEPLOYMENT.md |
| Build error "file is locked by HrmsAi.Host..." | A host from a previous debug run is still running. Stop it (Shift+F5), then build |
| `Restart-WebAppPool` says you have to start the object first | The pool is stopped. Use `Start-WebAppPool` |
| 422 on upload | The file is damaged, password protected, has no text (a scan), or is not what its extension says |
| The admin page answers 404 on the server | You opened it through the tunnel or a proxy, or from another computer. Open it in a browser on the server itself |
| The admin page answers 503 | `Admin__Password` is not set on the app pool |

## Capacity notes
On 8 CPU cores without a GPU, a resume takes about 20 to 50 seconds, and most of that is the AI **writing** the answer (about 14 tokens per second for a 3B model), not reading the resume. Throughput is about 2 resumes per minute with one worker. Options when that is not enough: more parallel slots (`Queue:Concurrency` together with `OLLAMA_NUM_PARALLEL`, measure with `scripts\benchmark.ps1`), a shorter AI answer, a second machine taking jobs from the same queue, or a GPU.