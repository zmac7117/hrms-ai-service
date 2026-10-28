# Deployment

Everything for this project lives under `C:\HRMS AI` on the server.

```text
C:\HRMS AI\
  app\v2\        source (this repository)      ollama\    Ollama program
  models\        downloaded AI models           tools\     nssm, ngrok
  tokens\        ngrok config (locked down)     logs\      service and tunnel logs
  data\          uploaded resumes (personal data)
  releases\      published builds (newest 3 kept)
  scripts\       start, stop, publish, control panel
```

## 1. Requirements
Windows Server or Windows 10/11, IIS with the **.NET 10 Hosting Bundle**, the .NET 10 SDK (on the build machine), SQL Server and SSMS, 16 GB RAM or more (30 GB used here), no GPU needed.

## 2. Database
Run `sql/001` to `sql/004` in SSMS, in order. The last one gives the IIS application pool `IIS APPPOOL\HRMSAI-Api` read and write rights.

## 3. Ollama as a Windows service
Install Ollama into the project folder, then run it as a service with NSSM, so it starts at boot without anyone logged in. Run PowerShell as Administrator.
```powershell
# install (download OllamaSetup.exe into C:\HRMS AI\tools first), then quit the tray app
& "C:\HRMS AI\tools\OllamaSetup.exe" /DIR="C:\HRMS AI\ollama"

$nssm = "C:\HRMS AI\tools\nssm\nssm.exe"
& $nssm install HRMSAI-Ollama "C:\HRMS AI\ollama\ollama.exe" serve
& $nssm set HRMSAI-Ollama AppDirectory "C:\HRMS AI\ollama"
& $nssm set HRMSAI-Ollama AppEnvironmentExtra "OLLAMA_MODELS=C:\HRMS AI\models" "OLLAMA_HOST=127.0.0.1:11434" "OLLAMA_KEEP_ALIVE=24h" "OLLAMA_NUM_PARALLEL=1" "OLLAMA_CONTEXT_LENGTH=8192"
& $nssm set HRMSAI-Ollama AppStdout "C:\HRMS AI\logs\ollama.out.log"
& $nssm set HRMSAI-Ollama AppStderr "C:\HRMS AI\logs\ollama.err.log"
& $nssm set HRMSAI-Ollama Start SERVICE_AUTO_START
& $nssm start HRMSAI-Ollama

& "C:\HRMS AI\ollama\ollama.exe" pull qwen2.5:3b
```
Close the Ollama tray app and remove it from Windows startup. If it keeps running, it holds port 11434 and the service shows as `Paused`.

## 4. IIS site
```powershell
Import-Module WebAdministration
New-WebAppPool -Name "HRMSAI-Api"
Set-ItemProperty IIS:\AppPools\HRMSAI-Api -Name managedRuntimeVersion -Value ""
Set-ItemProperty IIS:\AppPools\HRMSAI-Api -Name startMode -Value AlwaysRunning
Set-ItemProperty IIS:\AppPools\HRMSAI-Api -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
New-Website -Name "HRMSAI-Api" -PhysicalPath "C:\HRMS AI\releases" -ApplicationPool "HRMSAI-Api" -Port 8090
Set-ItemProperty "IIS:\Sites\HRMSAI-Api" -Name applicationDefaults.preloadEnabled -Value $true
```
`AlwaysRunning` and `preloadEnabled` matter: the background worker only runs while the application is running. The first publish (below) points the site at a real release folder.

## 5. Secrets (environment variables of the app pool)
```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
& $appcmd set config -section:system.applicationHost/applicationPools "/+[name='HRMSAI-Api'].environmentVariables.[name='Admin__Password',value='<a strong password>']" /commit:apphost
```
To change a value later, remove the entry (`/-[name='HRMSAI-Api'].environmentVariables.[name='Admin__Password']`), add it again, and restart the pool. Avoid the characters `'` `"` `%` `&` `^` in the password.

## 6. Publish
`scripts\publish.bat` builds the resume-only build (`publish.bat -Build Full` for the full one) into `C:\HRMS AI\releases\<date>-<build>`, removes the development settings, sets permissions, switches the site, waits for the health check, and **rolls back automatically** if the new release does not answer. The last 3 releases are kept.

## 7. Public access with ngrok (optional)
Keep the token out of command lines and out of the repository.
```powershell
New-Item -ItemType Directory -Force "C:\HRMS AI\tokens" | Out-Null
icacls "C:\HRMS AI\tokens" /inheritance:r /grant "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F"
$t = Read-Host "Paste your ngrok authtoken"
& "C:\HRMS AI\tools\ngrok\ngrok.exe" config add-authtoken $t --config "C:\HRMS AI\tokens\ngrok.yml"
Remove-Variable t

$nssm = "C:\HRMS AI\tools\nssm\nssm.exe"
& $nssm install HRMSAI-Tunnel "C:\HRMS AI\tools\ngrok\ngrok.exe" http 8090 --config ngrok.yml
& $nssm set HRMSAI-Tunnel AppDirectory "C:\HRMS AI\tokens"
& $nssm set HRMSAI-Tunnel AppStdout "C:\HRMS AI\logs\ngrok.out.log"
& $nssm set HRMSAI-Tunnel AppStderr "C:\HRMS AI\logs\ngrok.err.log"
& $nssm set HRMSAI-Tunnel Start SERVICE_AUTO_START
& $nssm start HRMSAI-Tunnel
```
The free plan gives one fixed dev domain, shown in the ngrok dashboard. Do not pass `--url` with it. Only the IIS port goes through the tunnel, never Ollama. Traffic is decrypted at the tunnel provider's edge, so check this against your data policy before sending real candidate data. A private network or VPN avoids the issue.

## 8. Verify
- `GET /api/health` shows the modules and `Ollama is running`.
- `http://localhost:8090/admin` opens on the server. Through the tunnel it must answer **404**.
- Create a key in the admin page, upload a resume with it, and wait for `Done`.
- Reboot the server without opening anything: Ollama, IIS and the tunnel must come back by themselves.

## 9. Updating and rolling back
Update: `scripts\publish.bat` (or the "Deploy latest build" button in the control panel). Roll back: the script prints the command at the end. It is `Set-ItemProperty 'IIS:\Sites\HRMSAI-Api' -Name physicalPath -Value '<previous release>'`, then restart the site.

When every client has its own key, remove the old shared key (`Security__ApiKey`) from the app pool the same way as in step 5.