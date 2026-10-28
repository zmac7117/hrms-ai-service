# =====================================================================
#  HRMS AI Control Panel  v2   (Windows PowerShell 5.1, needs administrator rights)
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

# ---------- helpers used by the buttons (these run on the window thread, always short) ----------
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

# ---------- service actions ----------
function Start-Svc($name, $nssm, $label, $logFile) {
    $state = Get-SvcState $name
    if ($state -eq 'Running') { Write-Log "$label is already running."; return }
    Write-Log "Starting $label..."
    if ($state -eq 'Paused' -and $nssm) { & $Nssm stop $name 2>&1 | Out-Null; Start-Sleep 2 }
    if ($nssm) { & $Nssm start $name 2>&1 | Out-Null }
    else { Start-Service -Name $name -ErrorAction SilentlyContinue }
    $ok = Wait-For { (Get-SvcState $name) -eq 'Running' } 40
    if ($ok) { Write-Log "$label started." }
    else { Write-Log "$label did not start (state: $(Get-SvcState $name))."; Show-LogTail $logFile }
}

function Stop-Svc($name, $nssm, $label) {
    if ((Get-SvcState $name) -eq 'Stopped') { Write-Log "$label is already stopped."; return }
    Write-Log "Stopping $label..."
    if ($nssm) { & $Nssm stop $name 2>&1 | Out-Null }
    else { Stop-Service -Name $name -Force -ErrorAction SilentlyContinue }
    $ok = Wait-For { (Get-SvcState $name) -eq 'Stopped' } 40
    Write-Log ($(if ($ok) { "$label stopped." } else { "$label did not stop in time." }))
}

function Svc-Action($name, $nssm, $action, $label, $logFile) {
    if ((Get-SvcState $name) -eq 'NotInstalled') { Write-Log "$label is not installed on this PC."; return }
    if ($action -in 'stop', 'restart')  { Stop-Svc $name $nssm $label }
    if ($action -in 'start', 'restart') { Start-Svc $name $nssm $label $logFile }
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
    [pscustomobject]$o
}

$cfg = @{
    SqlSvc = $SqlSvc; OllamaSvc = $OllamaSvc; TunnelSvc = $TunnelSvc
    OllamaUrl = $OllamaUrl; ApiUrl = $ApiUrl
    Appcmd = $Appcmd; SiteName = $SiteName; PoolName = $PoolName
}

function Start-Refresh {
    if ($script:refreshing -or $script:busy) { return }
    $script:refreshing = $true
    $script:ps = [powershell]::Create()
    [void]$script:ps.AddScript($collector).AddArgument($cfg)
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
                try { $until = "  |  stays loaded until " + ([datetime]$l.expires_at).ToString("dd MMM HH:mm") } catch {}
                [void]$it.SubItems.Add("In memory")
                [void]$it.SubItems.Add("RAM " + (Fmt-Bytes $l.size) + $until)
                $it.ForeColor = $cGreen
            }
            else {
                [void]$it.SubItems.Add("On disk (idle)")
                [void]$it.SubItems.Add("")
            }
            [void]$lv.Items.Add($it)
            if ($sel -and $sel -eq $m.name) { $it.Selected = $true }
        }
    }
    $lv.EndUpdate()
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

$form = New-Object System.Windows.Forms.Form
$form.Text = "HRMS AI Control Panel"
$form.ClientSize = New-Object System.Drawing.Size(940, 706)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedSingle"
$form.MaximizeBox = $false
$form.Font = $fontN

$lblTitle  = New-Label "HRMS AI Control Panel" 16 10 500 32 $fontT
$lblBanner = New-Label "Checking..." 16 46 700 24 $fontB

$gbS = New-Object System.Windows.Forms.GroupBox
$gbS.Text = "Services"
$gbS.Location = New-Object System.Drawing.Point(16, 78)
$gbS.Size = New-Object System.Drawing.Size(908, 262)

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

$gbM = New-Object System.Windows.Forms.GroupBox
$gbM.Text = "Ollama models"
$gbM.Location = New-Object System.Drawing.Point(16, 350)
$gbM.Size = New-Object System.Drawing.Size(908, 192)

$lv = New-Object System.Windows.Forms.ListView
$lv.View = [System.Windows.Forms.View]::Details
$lv.FullRowSelect = $true
$lv.HideSelection = $false
$lv.MultiSelect = $false
$lv.GridLines = $true
$lv.Location = New-Object System.Drawing.Point(12, 22)
$lv.Size = New-Object System.Drawing.Size(884, 118)
foreach ($c in @(@("Model", 190), @("Size", 80), @("Parameters", 120), @("State", 110), @("Details", 360))) { [void]$lv.Columns.Add($c[0], $c[1]) }

$bLoad    = New-Btn "Load into memory"   12  150 150 28
$bUnload  = New-Btn "Unload from memory" 168 150 160 28
$bRefresh = New-Btn "Refresh"            334 150 90  28
$bLoad.Add_Click({ Model-Action 'load' })
$bUnload.Add_Click({ Model-Action 'unload' })
$bRefresh.Add_Click({ $script:lastRefresh = (Get-Date).AddSeconds(-60) })
$gbM.Controls.AddRange(@($lv, $bLoad, $bUnload, $bRefresh))

$bStartAll = New-Btn "Start all"            16  552 100 30
$bStopAll  = New-Btn "Stop all"             122 552 100 30
$bAdmin    = New-Btn "Open admin page"      228 552 130 30
$bHealth   = New-Btn "Open health"          364 552 110 30
$bLogs     = New-Btn "Open logs folder"     480 552 130 30
$bDeploy   = New-Btn "Deploy latest build"  616 552 150 30
$bStartAll.Add_Click({ Start-All })
$bStopAll.Add_Click({ Stop-All })
$bAdmin.Add_Click({ Start-Process "$ApiUrl/admin" })
$bHealth.Add_Click({ Start-Process "$ApiUrl/api/health" })
$bLogs.Add_Click({ Start-Process explorer.exe $LogsDir })
$bDeploy.Add_Click({
    $r = [System.Windows.Forms.MessageBox]::Show("Publish the latest build from the source code and switch IIS to it?`r`nA window will show the progress. If the new version does not start, the old one is restored.", "Deploy latest build", "YesNo", "Question")
    if ($r -eq 'Yes') { Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$Root\scripts\publish.ps1`"" }
})

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = "Vertical"
$txtLog.Font = New-Object System.Drawing.Font("Consolas", 9)
$txtLog.Location = New-Object System.Drawing.Point(16, 592)
$txtLog.Size = New-Object System.Drawing.Size(908, 82)

$lblNote = New-Label "Closing this window does not stop anything. Status refreshes every 8 seconds." 16 680 700 20 $fontN
$lblNote.ForeColor = $cGray

$form.Controls.AddRange(@($lblTitle, $lblBanner, $gbS, $gbM, $bStartAll, $bStopAll, $bAdmin, $bHealth, $bLogs, $bDeploy, $txtLog, $lblNote))

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 700
$timer.Add_Tick({ Poll-Refresh })
$form.Add_Shown({ $timer.Start() })

try { [void]$form.ShowDialog() }
catch { [System.Windows.Forms.MessageBox]::Show("The control panel stopped: " + $_.Exception.Message, "HRMS AI Control Panel") | Out-Null }
$timer.Stop()
if ($script:ps) { try { $script:ps.Stop() } catch {}; try { $script:ps.Dispose() } catch {} }
