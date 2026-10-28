param(
    [ValidateSet("Resume", "Full")][string]$Build = "Resume"
)

# ---- needs administrator rights ----
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Build $Build"
    exit
}

$ErrorActionPreference = "Stop"
$Root   = "C:\HRMS AI"
$Site   = "HRMSAI-Api"
$Pool   = "HRMSAI-Api"
$ApiUrl = "http://localhost:8090"
$Appcmd = "$env:windir\system32\inetsrv\appcmd.exe"

function Test-Api {
    try { $r = Invoke-WebRequest "$ApiUrl/api/health" -UseBasicParsing -TimeoutSec 5; return [int]$r.StatusCode }
    catch [System.Net.WebException] { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode } else { return 0 } }
    catch { return 0 }
}

try {
    Import-Module WebAdministration
    $project = "$Root\app\v2\src\HrmsAi.Host.$Build"
    if (-not (Test-Path $project)) { throw "Project not found: $project" }
    if (-not (Get-Item "IIS:\Sites\$Site" -ErrorAction SilentlyContinue)) { throw "IIS site '$Site' not found." }

    $stamp   = Get-Date -Format "yyyy-MM-dd_HHmm"
    $release = "$Root\releases\$stamp-$Build"

    Write-Host "1/6  Building and publishing the $Build build..." -ForegroundColor Cyan
    dotnet publish $project -c Release -o $release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed. Nothing was changed on the server." }
    # the development settings hold test passwords and must not sit in the live folder
    Remove-Item "$release\appsettings.Development.json" -ErrorAction SilentlyContinue

    Write-Host "2/6  Setting folder permissions..." -ForegroundColor Cyan
    icacls $release       /grant "IIS AppPool\${Pool}:(OI)(CI)RX" | Out-Null
    icacls "$Root\logs"   /grant "IIS AppPool\${Pool}:(OI)(CI)M"  | Out-Null
    icacls "$Root\data"   /grant "IIS AppPool\${Pool}:(OI)(CI)M"  | Out-Null

    Write-Host "3/6  Checking the admin password setting..." -ForegroundColor Cyan
    $cfg = (& $Appcmd list apppool $Pool /config /xml) -join "`n"
    if ($cfg -notmatch 'Admin__Password') {
        Write-Host "     WARNING: Admin__Password is not set on the app pool. The /admin page will answer 503 until you set it." -ForegroundColor Yellow
    }

    Write-Host "4/6  Switching IIS to the new release..." -ForegroundColor Cyan
    $previous = (Get-ItemProperty "IIS:\Sites\$Site").physicalPath
    Stop-WebSite $Site -ErrorAction SilentlyContinue
    if ((Get-WebAppPoolState $Pool).Value -ne "Stopped") { Stop-WebAppPool $Pool }
    Start-Sleep 2
    Set-ItemProperty "IIS:\Sites\$Site" -Name physicalPath -Value $release
    Start-WebAppPool $Pool
    Start-WebSite $Site

    Write-Host "5/6  Waiting for the new release to answer..." -ForegroundColor Cyan
    $code = 0
    for ($i = 0; $i -lt 30; $i++) {
        $code = Test-Api
        if ($code -eq 200 -or $code -eq 503) { break }
        Start-Sleep 2
    }
    if ($code -eq 0) {
        Write-Host "     No answer. Rolling back to: $previous" -ForegroundColor Red
        Stop-WebSite $Site -ErrorAction SilentlyContinue
        Set-ItemProperty "IIS:\Sites\$Site" -Name physicalPath -Value $previous
        Restart-WebAppPool $Pool
        Start-WebSite $Site
        throw "The new release did not start, so the previous version is running again. Check C:\HRMS AI\logs, or turn on stdout logging in the release's web.config."
    }
    if ($code -eq 503) { Write-Host "     The API is up, but the AI engine (Ollama) is not answering. Start Ollama." -ForegroundColor Yellow }

    Write-Host "6/6  Cleaning up old releases (keeping the newest 3)..." -ForegroundColor Cyan
    Get-ChildItem "$Root\releases" -Directory | Sort-Object CreationTime -Descending | Select-Object -Skip 3 |
        Where-Object { $_.FullName -ne $previous } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "DONE. Live release: $release" -ForegroundColor Green
    Write-Host "To go back:  Set-ItemProperty 'IIS:\Sites\$Site' -Name physicalPath -Value '$previous'   (then restart the site)"
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
}
Read-Host "Press Enter to close"
