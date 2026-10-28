Import-Module WebAdministration -ErrorAction SilentlyContinue
$site = "HRMSAI-Api"; $pool = "HRMSAI-Api"

Write-Host "HRMS AI status" -ForegroundColor Cyan
Write-Host "Services"
foreach ($n in "HRMSAI-Ollama", "HRMSAI-Tunnel") {
    $s = Get-Service $n -ErrorAction SilentlyContinue
    Write-Host ("  {0,-16} {1}" -f $n, $(if ($s) { $s.Status } else { "not installed" }))
}
Write-Host "IIS"
Write-Host "  site: $((Get-WebsiteState -Name $site).Value), app pool: $((Get-WebAppPoolState -Name $pool).Value)"

Write-Host "Ports"
foreach ($port in 11434, 8090) {
    $l = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    Write-Host ("  {0,-6} {1}" -f $port, $(if ($l) { "listening" } else { "not listening" }))
}

Write-Host "API health"
try {
    $h = Invoke-RestMethod "http://localhost:8090/api/health" -TimeoutSec 5
    Write-Host "  app=$($h.app), ollama=$($h.ollama), model=$($h.model)"
} catch { Write-Host "  not responding" -ForegroundColor Red }

Write-Host "Memory used by Ollama"
$m = Get-Process -Name ollama* -ErrorAction SilentlyContinue | Measure-Object WorkingSet64 -Sum
Write-Host ("  {0} GB" -f [math]::Round(($m.Sum / 1GB), 1))
Read-Host "Press Enter to close"
