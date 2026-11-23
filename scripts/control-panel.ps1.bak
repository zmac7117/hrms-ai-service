# =====================================================================
#  HRMS AI Control Panel  v3   (Windows PowerShell 5.1, needs administrator rights)
#  Status checks run in a background thread, so the window never freezes.
# =====================================================================
$ErrorActionPreference = "Continue"

# ---------- settings: change here if your names differ ----------
$Root      = "C:\HRMS AI"
$Nssm      = "$Root\tools\nssm\nssm.exe"
$Appcmd    = "$env:windir\system32\inetsrv\appcmd.exe"
$SiteName  = "HRMSAI-Api"
$PoolName  = "HRMSAI-Api"
$ApiUrl    = "http://127.0.0.1:8090"
$OllamaUrl = "http://127.0.0.1:11434"
$OllamaSvc = "HRMSAI-Ollama"
$TunnelSvc = "HRMSAI-Tunnel"
$SqlSvc    = ""                       # empty = detect automatically
$PublicUrl = "https://joystick-deed-striking.ngrok-free.dev"
$LogsDir   = "$Root\logs"
$SrcDir    = "$Root\app\v2\src"
$Releases  = "$Root\releases"
$OllamaExe = "$Root\ollama\ollama.exe"
$ToolsDir  = "$Root\tools"

# ---------- run as administrator ----------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File `"$PSCommandPath`""
    exit
}

[System.Net.WebRequest]::DefaultWebProxy = $null      # no proxy lookup for local calls
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Net.Http
[System.Windows.Forms.Application]::EnableVisualStyles()

if (-not $SqlSvc) {
    $found = Get-Service | Where-Object { $_.Name -eq 'MSSQLSERVER' -or $_.Name -like 'MSSQL$*' } | Select-Object -First 1
    if ($found) { $SqlSvc = $found.Name }
}

$script:busy        = $false
$script:refreshing  = $false
$script:inPoll      = $false
$script:lastRefresh = (Get-Date).AddSeconds(-60)
$script:lastTools   = (Get-Date).AddMinutes(-10)
$script:toolsDue    = $true
$script:lastLive    = $null
$script:ps          = $null
$script:async       = $null
$script:actionControls = New-Object System.Collections.ArrayList
$DotChar = [string][char]0x25CF
$cGreen  = [System.Drawing.Color]::FromArgb(30, 142, 78)
$cRed    = [System.Drawing.Color]::FromArgb(192, 57, 43)
$cOrange = [System.Drawing.Color]::FromArgb(214, 137, 16)
$cGray   = [System.Drawing.Color]::FromArgb(130, 130, 130)
$fontN   = New-Object System.Drawing.Font("Segoe UI", 9)
$fontB   = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$fontT   = New-Object System.Drawing.Font("Segoe UI", 15, [System.Drawing.FontStyle]::Bold)
$fontDot = New-Object System.Drawing.Font("Segoe UI", 14)

# ---------- helpers (window thread, always short) ----------
function Pump { [System.Windows.Forms.Application]::DoEvents() }

function Write-Log($msg) {
    if ($txtLog) {
        $txtLog.AppendText(("[{0}] {1}`r`n" -f (Get-Date).ToString("HH:mm:ss"), $msg))
        Pump
    }
}

function Wait-For([scriptblock]$cond, [int]$seconds) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        if (& $cond) { return $true }
        Start-Sleep -Milliseconds 300
        Pump
    }
    return $false
}

function Get-Json($url, $timeout = 2) {
    try { return Invoke-RestMethod -Uri $url -TimeoutSec $timeout -ErrorAction Stop } catch { return $null }
}

function Test-Http($url, $timeout = 2) {
    try {
        $r = Invoke-WebRequest -Uri $url -TimeoutSec $timeout -UseBasicParsing -ErrorAction Stop
        return @{ Ok = $true; Code = [int]$r.StatusCode; Body = $r.Content }
    }
    catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($resp) { return @{ Ok = $true; Code = [int]$resp.StatusCode; Body = $null } }
        return @{ Ok = $false; Code = 0; Body = $null }
    }
    catch { return @{ Ok = $false; Code = 0; Body = $null } }
}

function Post-Json($url, $obj, $timeoutSec) {
    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [TimeSpan]::FromSeconds($timeoutSec)
    try {
        $body = New-Object System.Net.Http.StringContent(($obj | ConvertTo-Json -Compress), [System.Text.Encoding]::UTF8, "application/json")
        $task = $client.PostAsync($url, $body)
        while (-not $task.IsCompleted) { Start-Sleep -Milliseconds 150; Pump }
        if ($task.IsFaulted -or $task.IsCanceled) { return $false }
        return $task.Result.IsSuccessStatusCode
    }
    catch { return $false }
    finally { $client.Dispose() }
}

function Get-SvcState($name) {
    if (-not $name) { return "NotInstalled" }
    $s = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($s) { return $s.Status.ToString() } else { return "NotInstalled" }
}

function Fmt-Bytes($b) {
    if (-not $b) { return "-" }
    if ($b -ge 1GB) { return ("{0:N1} GB" -f ($b / 1GB)) }
    return ("{0:N0} MB" -f ($b / 1MB))
}

function Get-IisState {
    $site = "NotFound"; $pool = "NotFound"
    $t = (& $Appcmd list site $SiteName 2>$null) -join " "
    if ($t -match 'state:(\w+)') { $site = $Matches[1] }
    $t = (& $Appcmd list apppool $PoolName 2>$null) -join " "
    if ($t -match 'state:(\w+)') { $pool = $Matches[1] }
    return [pscustomobject]@{ Site = $site; Pool = $pool }
}

function Show-LogTail($file) {
    if ($file -and (Test-Path $file)) {
        Get-Content $file -Tail 3 -ErrorAction SilentlyContinue | ForEach-Object { Write-Log "   log: $_" }
    }
}

function Kill-Ollama {
    Get-Process -Name "ollama*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

function Get-Builds {
    if (-not (Test-Path $SrcDir)) { return @() }
    $prefix = "HrmsAi.Host."
    return @(Get-ChildItem $SrcDir -Directory -Filter "$prefix*" | ForEach-Object { $_.Name.Substring($prefix.Length) } | Sort-Object)
}

function Get-ReleaseList {
    if (-not (Test-Path $Releases)) { return @() }
    return @(Get-ChildItem $Releases -Directory | Sort-Object CreationTime -Descending | ForEach-Object { $_.Name })
}

function Fill-Combo($combo, $items, $prefer) {
    $keep = $combo.SelectedItem
    $combo.Items.Clear()
    foreach ($i in @($items)) { [void]$combo.Items.Add($i) }
    if ($keep -and $combo.Items.Contains($keep)) { $combo.SelectedItem = $keep }
    elseif ($prefer -and $combo.Items.Contains($prefer)) { $combo.SelectedItem = $prefer }
    elseif ($combo.Items.Count -gt 0) { $combo.SelectedIndex = 0 }
}

function Refresh-Builds   { Fill-Combo $cmbBuild (Get-Builds) "Resume" }
function Refresh-Releases { Fill-Combo $cmbRelease (Get-ReleaseList) $null }

# ---------- service actions ----------
# NOTE: the parameter is called $useNssm. PowerShell names are not case sensitive,
# so a parameter called $nssm would hide the $Nssm path above.
function Start-Svc($name, $useNssm, $label, $logFile) {
    $state = Get-SvcState $name
    if ($state -eq 'Running') { Write-Log "$label is already running."; return }
    Write-Log "Starting $label..."
    if ($state -eq 'Paused' -and $useNssm) { & $Nssm stop $name 2>&1 | Out-Null; Start-Sleep 2 }
    if ($useNssm) { & $Nssm start $name 2>&1 | Out-Null }
    else { Start-Service -Name $name -ErrorAction SilentlyContinue }
    $ok = Wait-For { (Get-SvcState $name) -eq 'Running' } 40
    if ($ok) { Write-Log "$label started." }
    else { Write-Log "$label did not start (state: $(Get-SvcState $name))."; Show-LogTail $logFile }
}

function Stop-Svc($name, $useNssm, $label) {
    if ((Get-SvcState $name) -eq 'Stopped') { Write-Log "$label is already stopped."; return }
    Write-Log "Stopping $label..."
    if ($useNssm) { & $Nssm stop $name 2>&1 | Out-Null }
    else { Stop-Service -Name $name -Force -ErrorAction SilentlyContinue }
    $ok = Wait-For { (Get-SvcState $name) -eq 'Stopped' } 40
    Write-Log ($(if ($ok) { "$label stopped." } else { "$label did not stop in time." }))
}

function Svc-Action($name, $useNssm, $action, $label, $logFile) {
    if ((Get-SvcState $name) -eq 'NotInstalled') { Write-Log "$label is not installed on this PC."; return }
    if ($action -in 'stop', 'restart')  { Stop-Svc $name $useNssm $label }
    if ($action -in 'start', 'restart') { Start-Svc $name $useNssm $label $logFile }
}

function Ollama-Action($action) {
    if ((Get-SvcState $OllamaSvc) -eq 'NotInstalled') { Write-Log "Ollama service '$OllamaSvc' is not installed."; return }
    if ($action -in 'stop', 'restart') { Stop-Svc $OllamaSvc $true "Ollama"; Kill-Ollama }
    if ($action -in 'start', 'restart') {
        if ((Get-SvcState $OllamaSvc) -ne 'Running' -and (Get-Process -Name "ollama*" -ErrorAction SilentlyContinue)) {
            Write-Log "Closing another Ollama that is holding the port..."
            Kill-Ollama
        }
        Start-Svc $OllamaSvc $true "Ollama" "$LogsDir\ollama.err.log"
        if ((Get-SvcState $OllamaSvc) -eq 'Running') {
            $ok = Wait-For { $null -ne (Get-Json $OllamaUrl 2) } 40
            Write-Log ($(if ($ok) { "Ollama is answering." } else { "The Ollama service is running but not answering yet." }))
        }
    }
}

function Api-Action($action) {
    if ($action -in 'stop', 'restart') {
        Write-Log "Stopping the API (IIS site and application pool)..."
        & $Appcmd stop site $SiteName 2>&1 | Out-Null
        & $Appcmd stop apppool $PoolName 2>&1 | Out-Null
        [void](Wait-For { (Get-IisState).Pool -in 'Stopped', 'NotFound' } 30)
        $s = Get-IisState
        Write-Log "API stopped (site: $($s.Site), app pool: $($s.Pool))."
    }
    if ($action -in 'start', 'restart') {
        if ((Get-SvcState 'W3SVC') -ne 'Running') {
            Write-Log "Starting IIS (W3SVC)..."
            Start-Service -Name W3SVC -ErrorAction SilentlyContinue
            [void](Wait-For { (Get-SvcState 'W3SVC') -eq 'Running' } 30)
        }
        Write-Log "Starting the API..."
        & $Appcmd start apppool $PoolName 2>&1 | Out-Null
        & $Appcmd start site $SiteName 2>&1 | Out-Null
        $ok = Wait-For { (Test-Http "$ApiUrl/api/health" 2).Ok } 60
        Write-Log ($(if ($ok) { "API is answering on $ApiUrl." } else { "The site started but is not answering. Check the IIS logs." }))
    }
}

# ---------- publish and releases ----------
function Deploy-Build {
    $build = $cmbBuild.SelectedItem
    if (-not $build) { Write-Log "Choose a build to publish first."; return }
    if (-not (Test-Path "$Root\scripts\publish.ps1")) { Write-Log "publish.ps1 was not found in $Root\scripts."; return }
    $r = [System.Windows.Forms.MessageBox]::Show(
        "Publish the '$build' build from the source code and switch IIS to it?`r`nA window shows the progress. If the new version does not start, the old one is restored.",
        "Deploy $build", "YesNo", "Question")
    if ($r -ne 'Yes') { return }
    Write-Log "Deploying the '$build' build (a progress window was opened)..."
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$Root\scripts\publish.ps1`" -Build $build"
    $script:lastRefresh = (Get-Date).AddSeconds(-60)
}

function Switch-Release {
    $name = $cmbRelease.SelectedItem
    if (-not $name) { Write-Log "Choose a release first."; return }
    $path = Join-Path $Releases $name
    if (-not (Test-Path $path)) { Write-Log "Release folder not found: $path"; return }
    $r = [System.Windows.Forms.MessageBox]::Show("Switch the live site to the release '$name'?`r`nThe site restarts for a few seconds.", "Switch release", "YesNo", "Question")
    if ($r -ne 'Yes') { return }
    if (-not (Begin-Work)) { return }
    try {
        Write-Log "Switching the live site to $name..."
        & $Appcmd stop site $SiteName 2>&1 | Out-Null
        & $Appcmd stop apppool $PoolName 2>&1 | Out-Null
        [void](Wait-For { (Get-IisState).Pool -in 'Stopped', 'NotFound' } 30)
        $out = & $Appcmd set vdir "$SiteName/" "/physicalPath:$path" 2>&1
        Write-Log (($out | Out-String).Trim())
        & $Appcmd start apppool $PoolName 2>&1 | Out-Null
        & $Appcmd start site $SiteName 2>&1 | Out-Null
        $ok = Wait-For { (Test-Http "$ApiUrl/api/health" 2).Ok } 60
        Write-Log ($(if ($ok) { "The site is answering on release $name." } else { "The site did not answer. Switch to another release, or check the logs." }))
    }
    catch { Write-Log "ERROR: $($_.Exception.Message)" }
    finally { End-Work }
}

function Pull-Model {
    $name = $txtPull.Text.Trim()
    if ($name -notmatch '^[A-Za-z0-9._:/\-]+$') { Write-Log "Type a model name, for example qwen2.5:7b."; return }
    if (-not (Test-Path $OllamaExe)) { Write-Log "ollama.exe was not found at $OllamaExe."; return }
    if ((Get-SvcState $OllamaSvc) -ne 'Running') { Write-Log "Start Ollama first. The download goes through the running service."; return }
    Write-Log "Downloading $name (a window shows the progress; close it when it says success)..."
    Start-Process powershell.exe -ArgumentList "-NoProfile -NoExit -Command `"& '$OllamaExe' pull '$name'`""
}

# ---------- busy handling ----------
function Set-Busy($on) {
    if ($on) { $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor } else { $form.Cursor = [System.Windows.Forms.Cursors]::Default }
    foreach ($c in $script:actionControls) { $c.Enabled = -not $on }
}
function Begin-Work { if ($script:busy) { return $false }; $script:busy = $true; Set-Busy $true; return $true }
function End-Work   { $script:busy = $false; Set-Busy $false; $script:lastRefresh = (Get-Date).AddSeconds(-60) }

function Do-Action($key, $action) {
    if ($key -eq 'sql' -and $action -in 'stop', 'restart') {
        $ans = [System.Windows.Forms.MessageBox]::Show("Other programs may use this SQL Server. Really $action it?", "Confirm", "YesNo", "Warning")
        if ($ans -ne 'Yes') { return }
    }
    if (-not (Begin-Work)) { return }
    try {
        switch ($key) {
            'sql'    { Svc-Action $SqlSvc $false $action "SQL Server" "" }
            'ollama' { Ollama-Action $action }
            'api'    { Api-Action $action }
            'tunnel' { Svc-Action $TunnelSvc $true $action "Public tunnel" "$LogsDir\ngrok.err.log" }
        }
    }
    catch { Write-Log "ERROR: $($_.Exception.Message)" }
    finally { End-Work }
}

function Start-All {
    if (-not (Begin-Work)) { return }
    try {
        Write-Log "=== Starting everything ==="
        Svc-Action $SqlSvc $false 'start' "SQL Server" ""
        Ollama-Action 'start'
        Api-Action 'start'
        Svc-Action $TunnelSvc $true 'start' "Public tunnel" "$LogsDir\ngrok.err.log"
        Write-Log "=== Done ==="
    }
    catch { Write-Log "ERROR: $($_.Exception.Message)" }
    finally { End-Work }
}

function Stop-All {
    if (-not (Begin-Work)) { return }
    try {
        Write-Log "=== Stopping the HRMS AI components (SQL Server is left running) ==="
        Svc-Action $TunnelSvc $true 'stop' "Public tunnel" ""
        Api-Action 'stop'
        Ollama-Action 'stop'
        Write-Log "=== Done ==="
    }
    catch { Write-Log "ERROR: $($_.Exception.Message)" }
    finally { End-Work }
}

function Model-Action($action) {
    if ($lv.SelectedItems.Count -eq 0 -or -not $lv.SelectedItems[0].Tag) { Write-Log "Select a model in the list first."; return }
    $name = [string]$lv.SelectedItems[0].Tag
    if (-not (Begin-Work)) { return }
    try {
        if ($action -eq 'load') {
            Write-Log "Loading $name into memory (10 to 30 seconds)..."
            $ok = Post-Json "$OllamaUrl/api/generate" @{ model = $name; keep_alive = "24h" } 180
            if (-not $ok) { $ok = Post-Json "$OllamaUrl/api/embed" @{ model = $name; input = "warm up"; keep_alive = "24h" } 180 }
        }
        else {
            Write-Log "Unloading $name from memory..."
            $ok = Post-Json "$OllamaUrl/api/generate" @{ model = $name; keep_alive = 0 } 60
            if (-not $ok) { $ok = Post-Json "$OllamaUrl/api/embed" @{ model = $name; input = "x"; keep_alive = 0 } 60 }
        }
        Write-Log ($(if ($ok) { "Done." } else { "That did not work. Is Ollama running?" }))
    }
    catch { Write-Log "ERROR: $($_.Exception.Message)" }
    finally { End-Work }
}

# ---------- background status collector (runs on another thread) ----------
$collector = {
    param($cfg)
    $ErrorActionPreference = "SilentlyContinue"
    [System.Net.WebRequest]::DefaultWebProxy = $null

    function SvcState($name) {
        if (-not $name) { return "NotInstalled" }
        $s = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($s) { return $s.Status.ToString() } else { return "NotInstalled" }
    }
    function Http($url, $timeout) {
        try {
            $r = Invoke-WebRequest -Uri $url -TimeoutSec $timeout -UseBasicParsing -ErrorAction Stop
            return @{ Ok = $true; Code = [int]$r.StatusCode; Body = $r.Content }
        }
        catch [System.Net.WebException] {
            $resp = $_.Exception.Response
            if ($resp) { return @{ Ok = $true; Code = [int]$resp.StatusCode; Body = $null } }
            return @{ Ok = $false; Code = 0; Body = $null }
        }
        catch { return @{ Ok = $false; Code = 0; Body = $null } }
    }
    function Json($url, $timeout) { try { Invoke-RestMethod -Uri $url -TimeoutSec $timeout -ErrorAction Stop } catch { $null } }
    function IisState($appcmd, $kind, $name) {
        $t = (& $appcmd list $kind $name 2>$null) -join " "
        if ($t -match 'state:(\w+)') { return $Matches[1] } else { return "NotFound" }
    }
    function Tool($name, $status, $details) { [pscustomobject]@{ Name = $name; Status = $status; Details = $details } }
    function FindExe($folder, $name) {
        if (Test-Path $folder) { Get-ChildItem $folder -Recurse -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName }
    }
    function FirstLine($exe, [string[]]$argList) {
        try { ((& $exe @argList 2>&1 | Out-String) -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1) } catch { $null }
    }

    $o = @{}
    $o.Sql       = SvcState $cfg.SqlSvc
    $o.OllamaSvc = SvcState $cfg.OllamaSvc
    $eng         = Http $cfg.OllamaUrl 2
    $o.OllamaUp  = $eng.Ok
    $o.Tags      = $null
    $o.Ps        = $null
    if ($eng.Ok) {
        $o.Tags = Json "$($cfg.OllamaUrl)/api/tags" 3
        $o.Ps   = Json "$($cfg.OllamaUrl)/api/ps" 3
    }
    $o.OllamaMem = (Get-Process -Name "ollama*" -ErrorAction SilentlyContinue | Measure-Object WorkingSet64 -Sum).Sum
    $o.W3        = SvcState 'W3SVC'
    $o.IisSite   = IisState $cfg.Appcmd 'site' $cfg.SiteName
    $o.IisPool   = IisState $cfg.Appcmd 'apppool' $cfg.PoolName
    $o.Api       = $null
    if ($o.W3 -eq 'Running' -and $o.IisSite -eq 'Started' -and $o.IisPool -eq 'Started') { $o.Api = Http "$($cfg.ApiUrl)/api/health" 5 }
    $o.Tunnel    = SvcState $cfg.TunnelSvc

    $o.LivePath = $null
    $vd = (& $cfg.Appcmd list vdir "$($cfg.SiteName)/" 2>$null) -join " "
    if ($vd -match 'physicalPath:(.+?)\)\s*$') { $o.LivePath = $Matches[1].Trim() }

    $o.Tools = $null
    if ($cfg.WithTools) {
        $tools = @()

        if (Test-Path $cfg.Nssm) { $tools += Tool "NSSM (service manager)" "ok" $cfg.Nssm }
        else { $tools += Tool "NSSM (service manager)" "missing" "Not found at $($cfg.Nssm)" }

        if (Test-Path $cfg.OllamaExe) { $tools += Tool "Ollama (AI engine)" "ok" ((FirstLine $cfg.OllamaExe @("--version")) + "   " + $cfg.OllamaExe) }
        else { $tools += Tool "Ollama (AI engine)" "missing" "Not found at $($cfg.OllamaExe)" }

        $ngrok = FindExe "$($cfg.Root)\tools\ngrok" "ngrok.exe"
        if ($ngrok) { $tools += Tool "ngrok (public tunnel)" "ok" ((FirstLine $ngrok @("--version")) + "   " + $ngrok) }
        else { $tools += Tool "ngrok (public tunnel)" "warn" "Not found under $($cfg.Root)\tools\ngrok (only needed for public access)" }

        $tess = FindExe "$($cfg.Root)\tools\tesseract" "tesseract.exe"
        if ($tess) {
            $langs = ((& $tess --list-langs 2>&1 | Out-String) -split "`r?`n" | Where-Object { $_.Trim() -and $_ -notmatch 'List of' }) -join ", "
            $tools += Tool "Tesseract (invoice OCR)" "ok" ((FirstLine $tess @("--version")) + "   languages: " + $langs)
        }
        else { $tools += Tool "Tesseract (invoice OCR)" "warn" "Not found under $($cfg.Root)\tools\tesseract (needed for scanned invoices)" }

        $ppm = FindExe "$($cfg.Root)\tools\poppler" "pdftoppm.exe"
        if ($ppm) { $tools += Tool "Poppler (PDF to image)" "ok" ((FirstLine $ppm @("-v")) + "   " + $ppm) }
        else { $tools += Tool "Poppler (PDF to image)" "warn" "Not found under $($cfg.Root)\tools\poppler (needed for scanned PDF invoices)" }

        try {
            $rt = @(& dotnet --list-runtimes 2>$null | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 10\.' })
            if ($rt.Count -gt 0) { $tools += Tool ".NET 10 runtime (ASP.NET Core)" "ok" (($rt | ForEach-Object { ($_ -split ' ')[1] }) -join ", ") }
            else { $tools += Tool ".NET 10 runtime (ASP.NET Core)" "missing" "Microsoft.AspNetCore.App 10.x not found" }
        } catch { $tools += Tool ".NET 10 runtime (ASP.NET Core)" "missing" "dotnet was not found" }

        $mod = "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
        if (Test-Path $mod) { $tools += Tool "IIS ASP.NET Core Module" "ok" "Hosting Bundle installed" }
        else { $tools += Tool "IIS ASP.NET Core Module" "missing" "Install the .NET Hosting Bundle, then run iisreset" }

        $git = FirstLine "git" @("--version")
        if ($git) { $tools += Tool "Git" "ok" $git } else { $tools += Tool "Git" "warn" "Not found (needed only for source control)" }

        $o.Tools = $tools
    }
    [pscustomobject]$o
}

$cfg = @{
    SqlSvc = $SqlSvc; OllamaSvc = $OllamaSvc; TunnelSvc = $TunnelSvc
    OllamaUrl = $OllamaUrl; ApiUrl = $ApiUrl
    Appcmd = $Appcmd; SiteName = $SiteName; PoolName = $PoolName
    Root = $Root; Nssm = $Nssm; OllamaExe = $OllamaExe
    WithTools = $false
}

function Start-Refresh {
    if ($script:refreshing -or $script:busy) { return }
    $script:refreshing = $true
    $c = $cfg.Clone()
    $c.WithTools = $script:toolsDue
    $script:toolsDue = $false
    $script:ps = [powershell]::Create()
    [void]$script:ps.AddScript($collector).AddArgument($c)
    $script:async = $script:ps.BeginInvoke()
}

function Poll-Refresh {
    if ($script:inPoll) { return }
    $script:inPoll = $true
    try {
        if ($script:refreshing) {
            if ($script:async.IsCompleted) {
                try {
                    $res = $script:ps.EndInvoke($script:async)
                    Apply-Status ($res | Select-Object -First 1)
                }
                catch { Write-Log "Status check failed: $($_.Exception.Message)" }
                finally {
                    $script:ps.Dispose()
                    $script:refreshing = $false
                    $script:lastRefresh = Get-Date
                }
            }
        }
        elseif (-not $script:busy -and ((Get-Date) - $script:lastRefresh).TotalSeconds -ge 8) {
            if (((Get-Date) - $script:lastTools).TotalSeconds -ge 300) { $script:toolsDue = $true }
            Start-Refresh
        }
    }
    finally { $script:inPoll = $false }
}

# ---------- show the collected status ----------
function Set-Row($key, $color, $text) {
    $ui[$key].Dot.ForeColor = $color
    $ui[$key].Detail.Text = $text
}

function Apply-Models($tags, $psj) {
    $sel = $null
    if ($lv.SelectedItems.Count -gt 0) { $sel = $lv.SelectedItems[0].Tag }
    $lv.BeginUpdate()
    $lv.Items.Clear()
    if (-not $tags) {
        $it = New-Object System.Windows.Forms.ListViewItem("Ollama is not running")
        $it.ForeColor = $cGray
        [void]$lv.Items.Add($it)
    }
    elseif (-not $tags.models) {
        $it = New-Object System.Windows.Forms.ListViewItem("No models installed")
        $it.ForeColor = $cGray
        [void]$lv.Items.Add($it)
    }
    else {
        $loaded = @{}
        if ($psj -and $psj.models) { foreach ($m in $psj.models) { $loaded[$m.name] = $m } }
        foreach ($m in $tags.models) {
            $it = New-Object System.Windows.Forms.ListViewItem($m.name)
            $it.Tag = $m.name
            [void]$it.SubItems.Add((Fmt-Bytes $m.size))
            [void]$it.SubItems.Add(("{0} {1}" -f $m.details.parameter_size, $m.details.quantization_level).Trim())
            if ($loaded.ContainsKey($m.name)) {
                $l = $loaded[$m.name]
                $until = ""
                try { $until = "  |  until " + ([datetime]$l.expires_at).ToString("dd MMM HH:mm") } catch {}
                [void]$it.SubItems.Add("In memory")
                [void]$it.SubItems.Add("RAM " + (Fmt-Bytes $l.size) + $until)
                $it.ForeColor = $cGreen
            }
            else {
                [void]$it.SubItems.Add("On disk")
                [void]$it.SubItems.Add("")
            }
            [void]$lv.Items.Add($it)
            if ($sel -and $sel -eq $m.name) { $it.Selected = $true }
        }
    }
    $lv.EndUpdate()
}

function Apply-Tools($tools) {
    $lvTools.BeginUpdate()
    $lvTools.Items.Clear()
    foreach ($t in $tools) {
        $it = New-Object System.Windows.Forms.ListViewItem([string]$t.Name)
        $label = if ($t.Status -eq 'ok') { 'OK' } elseif ($t.Status -eq 'warn') { 'Check' } else { 'Missing' }
        [void]$it.SubItems.Add($label)
        [void]$it.SubItems.Add([string]$t.Details)
        $it.ForeColor = if ($t.Status -eq 'ok') { $cGreen } elseif ($t.Status -eq 'warn') { $cOrange } else { $cRed }
        [void]$lvTools.Items.Add($it)
    }
    $lvTools.EndUpdate()
    $script:lastTools = Get-Date
}

function Apply-Status($s) {
    if (-not $s) { return }
    $good = 0; $total = 0

    # SQL Server
    if ($s.Sql -eq 'NotInstalled') { Set-Row 'sql' $cGray "SQL Server service not found on this PC" }
    else {
        $total++
        if ($s.Sql -eq 'Running') { $good++; Set-Row 'sql' $cGreen "Running  |  service $SqlSvc" }
        else { Set-Row 'sql' $cRed "$($s.Sql)  |  service $SqlSvc" }
    }

    # Ollama
    if ($s.OllamaSvc -eq 'NotInstalled') { Set-Row 'ollama' $cGray "Ollama service '$OllamaSvc' is not installed" }
    else {
        $total++
        $loadedNames = @()
        if ($s.Ps -and $s.Ps.models) { $loadedNames = @($s.Ps.models | ForEach-Object { $_.name }) }
        $memText = if ($s.OllamaMem) { "  |  RAM " + (Fmt-Bytes $s.OllamaMem) } else { "" }
        if ($s.OllamaSvc -eq 'Running' -and $s.OllamaUp) {
            $m = if ($loadedNames.Count) { "model in memory: " + ($loadedNames -join ', ') } else { "no model in memory (loads on the first request)" }
            $good++
            Set-Row 'ollama' $cGreen "Running  |  $m$memText"
        }
        elseif ($s.OllamaSvc -eq 'Running') { Set-Row 'ollama' $cOrange "Service running, but the engine is not answering yet" }
        elseif ($s.OllamaSvc -eq 'Paused')  { Set-Row 'ollama' $cOrange "Stuck (Paused). Press Start to repair it" }
        elseif ($s.OllamaUp)                { Set-Row 'ollama' $cOrange "Another Ollama (not this service) is answering on port 11434. Press Stop, then Start" }
        else                                { Set-Row 'ollama' $cRed "$($s.OllamaSvc)" }
    }

    # API (IIS)
    $total++
    if ($s.W3 -ne 'Running') { Set-Row 'api' $cRed "IIS (W3SVC) is $($s.W3)" }
    elseif ($s.IisSite -eq 'NotFound') { $total--; Set-Row 'api' $cGray "IIS site '$SiteName' not found" }
    elseif ($s.IisSite -eq 'Started' -and $s.IisPool -eq 'Started') {
        $h = $s.Api
        if ($h -and $h.Ok -and $h.Code -eq 200) {
            $j = $null
            try { $j = $h.Body | ConvertFrom-Json } catch {}
            $mods = if ($j -and $j.modules) { $j.modules -join ', ' } else { '?' }
            $mdl  = if ($j -and $j.model) { $j.model } else { '?' }
            $good++
            Set-Row 'api' $cGreen "Running on $ApiUrl  |  modules: $mods  |  AI model: $mdl"
        }
        elseif ($h -and $h.Ok) { Set-Row 'api' $cOrange "Running, but the health check returned $($h.Code) (is Ollama running?)" }
        else                   { Set-Row 'api' $cOrange "Site started, but not answering on $ApiUrl" }
    }
    else { Set-Row 'api' $cRed "Site: $($s.IisSite)  |  App pool: $($s.IisPool)" }

    # Tunnel
    if ($s.Tunnel -eq 'NotInstalled') { Set-Row 'tunnel' $cGray "Not installed (optional)" }
    else {
        $total++
        if ($s.Tunnel -eq 'Running')    { $good++; Set-Row 'tunnel' $cGreen "Running  |  $PublicUrl" }
        elseif ($s.Tunnel -eq 'Paused') { Set-Row 'tunnel' $cOrange "Stuck (Paused). Check $LogsDir\ngrok.err.log, then press Start" }
        else                            { Set-Row 'tunnel' $cRed "$($s.Tunnel)" }
    }

    if ($total -gt 0 -and $good -eq $total) { $lblBanner.ForeColor = $cGreen;  $lblBanner.Text = "$DotChar Everything is running ($good of $total)" }
    elseif ($good -eq 0)                    { $lblBanner.ForeColor = $cRed;    $lblBanner.Text = "$DotChar Everything is stopped" }
    else                                    { $lblBanner.ForeColor = $cOrange; $lblBanner.Text = "$DotChar $good of $total running. See the details below" }

    # which release the site serves
    if ($s.LivePath) {
        $leaf = Split-Path $s.LivePath -Leaf
        if ($leaf -match '^\d{4}-\d{2}-\d{2}_\d{4}-(.+)$') { $lblLive.Text = "$($Matches[1]) build   ($leaf)" } else { $lblLive.Text = $s.LivePath }
        if ($script:lastLive -ne $s.LivePath) { $script:lastLive = $s.LivePath; Refresh-Releases }
    }
    else { $lblLive.Text = "unknown (the IIS site was not found)" }

    if ($s.Tools) { Apply-Tools $s.Tools }
    Apply-Models $s.Tags $s.Ps
}

# ---------- build the window ----------
function New-Label($text, $x, $y, $w, $h, $font) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $text
    $l.Location = New-Object System.Drawing.Point($x, $y)
    $l.Size = New-Object System.Drawing.Size($w, $h)
    $l.Font = $font
    return $l
}
function New-Btn($text, $x, $y, $w, $h) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $text
    $b.Location = New-Object System.Drawing.Point($x, $y)
    $b.Size = New-Object System.Drawing.Size($w, $h)
    [void]$script:actionControls.Add($b)
    return $b
}
function New-Combo($x, $y, $w) {
    $c = New-Object System.Windows.Forms.ComboBox
    $c.DropDownStyle = "DropDownList"
    $c.Location = New-Object System.Drawing.Point($x, $y)
    $c.Size = New-Object System.Drawing.Size($w, 24)
    [void]$script:actionControls.Add($c)
    return $c
}
function New-Group($text, $x, $y, $w, $h) {
    $g = New-Object System.Windows.Forms.GroupBox
    $g.Text = $text
    $g.Location = New-Object System.Drawing.Point($x, $y)
    $g.Size = New-Object System.Drawing.Size($w, $h)
    return $g
}
function New-List($x, $y, $w, $h, $columns) {
    $l = New-Object System.Windows.Forms.ListView
    $l.View = [System.Windows.Forms.View]::Details
    $l.FullRowSelect = $true
    $l.HideSelection = $false
    $l.MultiSelect = $false
    $l.GridLines = $true
    $l.Location = New-Object System.Drawing.Point($x, $y)
    $l.Size = New-Object System.Drawing.Size($w, $h)
    foreach ($c in $columns) { [void]$l.Columns.Add($c[0], $c[1]) }
    return $l
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "HRMS AI Control Panel"
$form.ClientSize = New-Object System.Drawing.Size(940, 900)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedSingle"
$form.MaximizeBox = $false
$form.AutoScroll = $true
$form.Font = $fontN

$lblTitle  = New-Label "HRMS AI Control Panel" 16 10 500 32 $fontT
$lblBanner = New-Label "Checking..." 16 46 700 24 $fontB

# ----- services -----
$gbS = New-Group "Services" 16 78 908 262
$ui = @{}
$defs = @(
    @{ Key = 'sql';    Title = 'SQL Server' },
    @{ Key = 'ollama'; Title = 'Ollama (AI engine)' },
    @{ Key = 'api';    Title = 'HRMS AI API (IIS)' },
    @{ Key = 'tunnel'; Title = 'Public tunnel (ngrok)' }
)
$i = 0
foreach ($d in $defs) {
    $y = 22 + $i * 58
    $dot    = New-Label $DotChar 12 ($y + 2) 24 30 $fontDot
    $dot.ForeColor = $cGray
    $name   = New-Label $d.Title 40 ($y + 8) 200 20 $fontB
    $detail = New-Label "Checking..." 245 ($y + 4) 400 46 $fontN
    $bStart   = New-Btn "Start"   655 ($y + 6) 76 28
    $bStop    = New-Btn "Stop"    735 ($y + 6) 76 28
    $bRestart = New-Btn "Restart" 815 ($y + 6) 76 28
    $bStart.Tag   = "$($d.Key)|start"
    $bStop.Tag    = "$($d.Key)|stop"
    $bRestart.Tag = "$($d.Key)|restart"
    foreach ($b in @($bStart, $bStop, $bRestart)) {
        $b.Add_Click({ param($s, $e) $p = $s.Tag -split '\|'; Do-Action $p[0] $p[1] })
    }
    $gbS.Controls.AddRange(@($dot, $name, $detail, $bStart, $bStop, $bRestart))
    $ui[$d.Key] = @{ Dot = $dot; Detail = $detail }
    $i++
}

# ----- models -----
$gbM = New-Group "Ollama models" 16 350 560 192
$lv = New-List 12 22 536 118 @(@("Model", 150), @("Size", 70), @("Parameters", 100), @("State", 80), @("Details", 130))
$bLoad    = New-Btn "Load"   12  148 80 28
$bUnload  = New-Btn "Unload" 98  148 80 28
$txtPull  = New-Object System.Windows.Forms.TextBox
$txtPull.Location = New-Object System.Drawing.Point(300, 150)
$txtPull.Size = New-Object System.Drawing.Size(180, 24)
[void]$script:actionControls.Add($txtPull)
$lblPull   = New-Label "Download:" 232 153 66 18 $fontN
$bPull     = New-Btn "Pull" 486 148 62 28
$bLoad.Add_Click({ Model-Action 'load' })
$bUnload.Add_Click({ Model-Action 'unload' })
$bPull.Add_Click({ Pull-Model })
$gbM.Controls.AddRange(@($lv, $bLoad, $bUnload, $lblPull, $txtPull, $bPull))

# ----- publish -----
$gbD = New-Group "Publish to IIS" 588 350 336 192
$cmbBuild   = New-Combo 12 42 200
$bDeploy    = New-Btn "Deploy" 220 40 100 28
$lblLive    = New-Label "Checking..." 12 96 312 34 $fontB
$cmbRelease = New-Combo 12 152 200
$bSwitch    = New-Btn "Switch" 220 150 100 28
$bDeploy.Add_Click({ Deploy-Build })
$bSwitch.Add_Click({ Switch-Release })
$gbD.Controls.AddRange(@(
    (New-Label "Build to publish" 12 22 200 18 $fontN), $cmbBuild, $bDeploy,
    (New-Label "Live now" 12 78 200 18 $fontN), $lblLive,
    (New-Label "Switch to an earlier release" 12 134 250 18 $fontN), $cmbRelease, $bSwitch))

# ----- tools -----
$gbT = New-Group "Tools" 16 552 908 160
$lvTools = New-List 12 22 884 96 @(@("Tool", 190), @("Status", 70), @("Details", 600))
$bRecheck = New-Btn "Re-check tools" 12 124 110 28
$bToolsDir = New-Btn "Open tools folder" 128 124 130 28
$bNgrok = New-Btn "Open ngrok dashboard" 264 124 160 28
$bRecheck.Add_Click({ $script:toolsDue = $true; $script:lastRefresh = (Get-Date).AddSeconds(-60); Refresh-Builds; Refresh-Releases })
$bToolsDir.Add_Click({ if (Test-Path $ToolsDir) { Start-Process explorer.exe $ToolsDir } })
$bNgrok.Add_Click({ Start-Process "http://127.0.0.1:4040" })
$gbT.Controls.AddRange(@($lvTools, $bRecheck, $bToolsDir, $bNgrok))

# ----- bottom buttons and log -----
$bStartAll = New-Btn "Start all"        16  722 96  30
$bStopAll  = New-Btn "Stop all"         118 722 96  30
$bAdmin    = New-Btn "Admin keys"       220 722 100 30
$bActivity = New-Btn "Admin activity"   326 722 110 30
$bClient   = New-Btn "Client page"      442 722 100 30
$bHealth   = New-Btn "Health"           548 722 80  30
$bLogs     = New-Btn "Logs folder"      634 722 96  30
$bRelDir   = New-Btn "Releases folder"  736 722 110 30
$bStartAll.Add_Click({ Start-All })
$bStopAll.Add_Click({ Stop-All })
$bAdmin.Add_Click({ Start-Process "$ApiUrl/admin" })
$bActivity.Add_Click({ Start-Process "$ApiUrl/admin/activity" })
$bClient.Add_Click({ Start-Process "$ApiUrl/dashboard" })
$bHealth.Add_Click({ Start-Process "$ApiUrl/api/health" })
$bLogs.Add_Click({ if (Test-Path $LogsDir) { Start-Process explorer.exe $LogsDir } })
$bRelDir.Add_Click({ if (Test-Path $Releases) { Start-Process explorer.exe $Releases } })

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = "Vertical"
$txtLog.Font = New-Object System.Drawing.Font("Consolas", 9)
$txtLog.Location = New-Object System.Drawing.Point(16, 762)
$txtLog.Size = New-Object System.Drawing.Size(908, 100)

$lblNote = New-Label "Closing this window does not stop anything. Status refreshes every 8 seconds, tools every 5 minutes." 16 868 800 20 $fontN
$lblNote.ForeColor = $cGray

$form.Controls.AddRange(@($lblTitle, $lblBanner, $gbS, $gbM, $gbD, $gbT,
    $bStartAll, $bStopAll, $bAdmin, $bActivity, $bClient, $bHealth, $bLogs, $bRelDir, $txtLog, $lblNote))

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 700
$timer.Add_Tick({ Poll-Refresh })
$form.Add_Shown({ Refresh-Builds; Refresh-Releases; $timer.Start() })

try { [void]$form.ShowDialog() }
catch { [System.Windows.Forms.MessageBox]::Show("The control panel stopped: " + $_.Exception.Message, "HRMS AI Control Panel") | Out-Null }
$timer.Stop()
if ($script:ps) { try { $script:ps.Stop() } catch {}; try { $script:ps.Dispose() } catch {} }
