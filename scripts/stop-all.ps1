$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}
Import-Module WebAdministration
$nssm = "C:\HRMS AI\tools\nssm\nssm.exe"
$site = "HRMSAI-Api"; $pool = "HRMSAI-Api"

function Stop-Svc($name) {
    $s = Get-Service $name -ErrorAction SilentlyContinue
    if (-not $s) { Write-Host "  $name : not installed" -ForegroundColor Yellow; return }
    if ($s.Status -ne 'Stopped') {
        & $nssm stop $name | Out-Null
        try { (Get-Service $name).WaitForStatus('Stopped', '00:00:30') } catch {}
    }
    Write-Host "  $name : $((Get-Service $name).Status)"
}

Write-Host "Stopping HRMS AI..." -ForegroundColor Cyan

Write-Host "1. Tunnel (public access off)"
Stop-Svc "HRMSAI-Tunnel"

Write-Host "2. IIS site and app pool"
if ((Get-WebsiteState -Name $site).Value -ne 'Stopped') { Stop-WebSite $site }
if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') { Stop-WebAppPool $pool }
Write-Host "  site: $((Get-WebsiteState -Name $site).Value), app pool: $((Get-WebAppPoolState -Name $pool).Value)"

Write-Host "3. Ollama (frees the model memory)"
Stop-Svc "HRMSAI-Ollama"

Write-Host "4. Leftover processes"
foreach ($n in "ngrok*", "ollama app", "ollama*") {
    Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
$left = Get-Process | Where-Object { $_.Name -like "ngrok*" -or $_.Name -like "ollama*" }
if ($left) { Write-Host "  still running: $($left.Name -join ', ')" -ForegroundColor Yellow } else { Write-Host "  none" }

Write-Host "`nAll stopped. SQL Server was not touched." -ForegroundColor Green
Read-Host "Press Enter to close"

