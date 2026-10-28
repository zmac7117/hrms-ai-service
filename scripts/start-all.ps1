$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}
Import-Module WebAdministration
$nssm = "C:\HRMS AI\tools\nssm\nssm.exe"
$site = "HRMSAI-Api"; $pool = "HRMSAI-Api"
$publicUrl = "https://joystick-deed-striking.ngrok-free.dev"

function Start-Svc($name) {
    $s = Get-Service $name -ErrorAction SilentlyContinue
    if (-not $s) { Write-Host "  $name : not installed" -ForegroundColor Yellow; return }
    if ($s.Status -eq 'Paused') { & $nssm stop $name | Out-Null; Start-Sleep 2 }   # clear a stuck state
    if ((Get-Service $name).Status -ne 'Running') {
        & $nssm start $name | Out-Null
        try { (Get-Service $name).WaitForStatus('Running', '00:00:30') } catch {}
    }
    $st = (Get-Service $name).Status
    $color = if ($st -eq 'Running') { 'Green' } else { 'Red' }
    Write-Host "  $name : $st" -ForegroundColor $color
}

function Wait-Url($url, $seconds, $headers = @{}) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        try { return Invoke-RestMethod $url -Headers $headers -TimeoutSec 5 } catch { Start-Sleep 2 }
    }
    return $null
}

Write-Host "Starting HRMS AI..." -ForegroundColor Cyan

Write-Host "1. Ollama"
Start-Svc "HRMSAI-Ollama"
$o = Wait-Url "http://localhost:11434" 40
Write-Host "  engine: $(if ($o) { $o } else { 'NOT RESPONDING' })"

Write-Host "2. IIS site and app pool"
if ((Get-WebAppPoolState -Name $pool).Value -ne 'Started') { Start-WebAppPool $pool }
if ((Get-WebsiteState -Name $site).Value -ne 'Started') { Start-WebSite $site }
Write-Host "  site: $((Get-WebsiteState -Name $site).Value), app pool: $((Get-WebAppPoolState -Name $pool).Value)"
$h = Wait-Url "http://localhost:8090/api/health" 60
if ($h) { Write-Host "  api health: app=$($h.app), ollama=$($h.ollama), model=$($h.model)" -ForegroundColor Green }
else { Write-Host "  api health: NOT RESPONDING" -ForegroundColor Red }

Write-Host "3. Tunnel"
Start-Svc "HRMSAI-Tunnel"
$p = Wait-Url "$publicUrl/api/health" 30 @{ "ngrok-skip-browser-warning" = "1" }
if ($p) { Write-Host "  public health: OK ($publicUrl)" -ForegroundColor Green }
else { Write-Host "  public health: not reachable (check C:\HRMS AI\logs\ngrok.err.log)" -ForegroundColor Yellow }

Write-Host "`nDone." -ForegroundColor Green
Read-Host "Press Enter to close"
