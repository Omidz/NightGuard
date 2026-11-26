# NightGuard - screen time tracker. Runs every minute as SYSTEM (when screen time limits are on).
# - Counts today's screen time (someone signed in at the console and the screen not locked)
# - Counts today's game time (screen in use AND a game is running)
# - Warns as limits get close, closes games when the game budget is used up,
#   and locks / shuts down / just warns when the screen budget is used up.
# The "day" starts at ResetHour (default 4 AM), so late-night play counts toward the evening before.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

function Save-Usage($Usage) {
    $tmp = "$NGUsageFile.tmp"
    $Usage | ConvertTo-Json -Depth 5 | Set-Content -Path $tmp -Encoding UTF8
    Move-Item -Force $tmp $NGUsageFile
}

function New-Usage([string]$Day) {
    [pscustomobject]@{
        Day = $Day; GameMinutes = 0.0; ScreenMinutes = 0.0; LastTick = $null; Sent = @()
        GameLimitNotified = $false; LastGameKillNotice = $null; LastScreenAction = $null; ShutdownIssuedAt = $null
    }
}

function ConvertTo-Time($Value) {
    if ($Value -is [datetime]) { return $Value }
    if ([string]::IsNullOrEmpty([string]$Value)) { return $null }
    try { return [datetime]::Parse([string]$Value, $null, [Globalization.DateTimeStyles]::RoundtripKind) } catch { return $null }
}

# Shows a popup in the signed-in user's session through the "Screen time notify" task.
function Send-Notice([string]$Message, [bool]$Lock = $false) {
    Write-NGLog "screen time: $Message"
    try {
        [pscustomobject]@{ Id = [guid]::NewGuid().ToString(); Time = (Get-Date).ToString('o'); Message = $Message; Lock = $Lock } |
            ConvertTo-Json | Set-Content -Path $NGNotifyFile -Encoding UTF8
        Start-ScheduledTask -TaskPath $NGTaskPath -TaskName 'Screen time notify'
    } catch {
        try { & "$env:SystemRoot\System32\msg.exe" '*' '/TIME:60' "NightGuard: $Message" } catch { }
    }
}

function Format-Left([double]$Minutes) {
    $m = [math]::Max(0, [math]::Ceiling($Minutes))
    if ($m -ge 60) { return ('{0}h {1:00}m' -f [math]::Floor($m / 60), ($m % 60)) }
    return "$m min"
}

try {
    if (Test-NGSafeMode) { exit 0 }
    $cfg = Get-NGConfig
    if (-not $cfg -or -not $cfg.ScreenTime.Enabled -or (Test-NGExpired $cfg)) { exit 0 }
    if (Get-NGUnsafeReason $cfg) { exit 0 }
    $st = $cfg.ScreenTime

    $now      = Get-NGNow   # NightGuard's clock (time zone pinned at install)
    $usageDay = Get-NGUsageDay $st $now
    $dayKey   = $usageDay.ToString('yyyy-MM-dd')
    $limits   = Get-NGLimits $st $usageDay
    $gameLimit = [double]$limits.Game; $screenLimit = [double]$limits.Screen
    $gameLimited = $gameLimit -gt 0; $screenLimited = $screenLimit -gt 0

    # ---- Load usage, roll over at the day boundary ----
    $u = $null
    if (Test-Path $NGUsageFile) { try { $u = Get-Content -Raw -Path $NGUsageFile | ConvertFrom-Json } catch { } }
    if ($null -eq $u -or $u.Day -ne $dayKey) {
        if ($null -ne $u) { Write-NGLog ("screen time: {0} finished, games {1}, screen {2}" -f $u.Day, (Format-NGMinutes $u.GameMinutes), (Format-NGMinutes $u.ScreenMinutes)) }
        $lastTick = if ($null -ne $u) { $u.LastTick } else { $null }
        $u = New-Usage $dayKey
        $u.LastTick = $lastTick
    }
    $u.Sent = @($u.Sent)

    # Time since last run, capped so sleep / power-off / missed runs are not counted
    $elapsed = 1.0
    $last = ConvertTo-Time $u.LastTick
    if ($null -ne $last) {
        $elapsed = ($now - $last).TotalMinutes
        if ($elapsed -lt 0) { $elapsed = 0 }
        if ($elapsed -gt 2) { $elapsed = 1 }
    }
    $u.LastTick = $now.ToString('o')

    # ---- Is someone using the screen? (signed in at the console, lock screen not showing)
    $consoleUser = $null
    try { $consoleUser = (Get-CimInstance Win32_ComputerSystem).UserName } catch { }
    $allProcs = @(Get-CimInstance Win32_Process)
    $lockScreenUp = @($allProcs | Where-Object { $_.Name -eq 'LogonUI.exe' }).Count -gt 0
    $screenActive = (-not [string]::IsNullOrEmpty($consoleUser)) -and (-not $lockScreenUp)

    # ---- Which games are running? ----
    $ignore    = @($st.IgnoreProcesses | ForEach-Object { "$_".ToLowerInvariant() }) + $NGNeverKill
    $gameNames = @($st.GameProcesses | ForEach-Object { "$_".ToLowerInvariant() -replace '\.exe$', '' })
    $folders   = @($st.GameFolders | Where-Object { $_ -and -not (Test-NGProtectedPath "$_") } | ForEach-Object { "$_".TrimEnd('\').ToLowerInvariant() + '\' })
    $launchers = @($st.LauncherProcesses | ForEach-Object { "$_".ToLowerInvariant() -replace '\.exe$', '' } | Where-Object { $NGNeverKill -notcontains $_ })

    $games = @()
    foreach ($p in $allProcs) {
        if ($p.SessionId -eq 0 -or [string]::IsNullOrEmpty($p.Name)) { continue }
        $base = ($p.Name -replace '\.exe$', '').ToLowerInvariant()
        if ($ignore -contains $base) { continue }
        $isGame = $gameNames -contains $base
        if (-not $isGame -and $p.ExecutablePath) {
            $path = $p.ExecutablePath.ToLowerInvariant()
            if (-not $path.StartsWith($env:SystemRoot.ToLowerInvariant())) {
                foreach ($f in $folders) { if ($path.StartsWith($f)) { $isGame = $true; break } }
            }
        }
        if ($isGame) { $games += $p }
    }

    # ---- Count time ----
    if ($screenActive) {
        $u.ScreenMinutes = [double]$u.ScreenMinutes + $elapsed
        if ($games.Count -gt 0) { $u.GameMinutes = [double]$u.GameMinutes + $elapsed }
    }
    $gameLeft   = $gameLimit - [double]$u.GameMinutes
    $screenLeft = $screenLimit - [double]$u.ScreenMinutes
    $warnAt     = @($st.WarnAtMinutesLeft | ForEach-Object { [int]$_ } | Sort-Object -Descending)

    # ---- Warnings (one popup per crossed threshold) ----
    function Test-Warn([string]$Kind, [double]$Left) {
        $crossed = @($warnAt | Where-Object { $Left -le $_ -and -not ($u.Sent -contains "$Kind-$_") })
        if ($crossed.Count -eq 0 -or $Left -le 0) { return $false }
        foreach ($t in $crossed) { $u.Sent += "$Kind-$t" }
        return $true
    }
    $action = [string]$st.LimitAction
    if ($gameLimited -and $games.Count -gt 0 -and $screenActive -and (Test-Warn 'game' $gameLeft)) {
        Send-Notice ("About {0} of game time left today. Save your game." -f (Format-Left $gameLeft))
    }
    if ($screenLimited -and $screenActive -and (Test-Warn 'screen' $screenLeft)) {
        $what = switch ($action) { 'Shutdown' { 'the PC will shut down' } 'Warn' { 'you will be reminded to stop' } default { 'the PC will lock' } }
        Send-Notice ("About {0} of screen time left today. After that {1}." -f (Format-Left $screenLeft), $what)
    }

    # ---- Game budget used up: close games ----
    if ($gameLimited -and $gameLeft -le 0) {
        $toKill = @($games)
        if ($st.CloseLaunchers) {
            $toKill += @($allProcs | Where-Object { $_.SessionId -ne 0 -and $launchers -contains (($_.Name -replace '\.exe$', '').ToLowerInvariant()) })
        }
        $killed = @()
        foreach ($p in $toKill) {
            if ($NGNeverKill -contains (($p.Name -replace '\.exe$', '').ToLowerInvariant())) { continue }
            try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop; $killed += $p.Name } catch { }
        }
        if ($killed.Count -gt 0) { Write-NGLog ("screen time: closed " + (($killed | Select-Object -Unique) -join ', ')) }
        $lastKill = ConvertTo-Time $u.LastGameKillNotice
        if (-not $u.GameLimitNotified) {
            $u.GameLimitNotified = $true
            $u.LastGameKillNotice = $now.ToString('o')
            Send-Notice "Game time for today is used up. Games will be closed until $($st.ResetHour):00."
        } elseif ($killed.Count -gt 0 -and ($null -eq $lastKill -or ($now - $lastKill).TotalMinutes -ge 3)) {
            $u.LastGameKillNotice = $now.ToString('o')
            Send-Notice 'Game time for today is used up, so that game was closed.'
        }
    }

    # ---- Screen budget used up ----
    if ($screenLimited -and $screenLeft -le 0) {
        $lastAction = ConvertTo-Time $u.LastScreenAction
        switch ($action) {
            'Warn' {
                if ($screenActive -and ($null -eq $lastAction -or ($now - $lastAction).TotalMinutes -ge 10)) {
                    $u.LastScreenAction = $now.ToString('o')
                    Send-Notice 'Screen time for today is used up. Time to stop.'
                }
            }
            'Shutdown' {
                $grace = [int]$st.ShutdownGraceMinutes
                $issued = ConvertTo-Time $u.ShutdownIssuedAt
                # Re-issue if an earlier shutdown was cancelled and the PC is still on
                if ($screenActive -and ($null -eq $issued -or ($now - $issued).TotalMinutes -ge ($grace + 2))) {
                    $u.ShutdownIssuedAt = $now.ToString('o')
                    Write-NGLog "screen time: limit reached, shutting down in $grace min"
                    try { & "$env:SystemRoot\System32\shutdown.exe" /s /f /t ($grace * 60) /c "NightGuard: screen time for today is used up. Shutting down in $grace minute(s)." 2>$null | Out-Null } catch { }
                }
            }
            default {
                # Lock again every minute while someone is using the screen
                if ($screenActive) {
                    $u.LastScreenAction = $now.ToString('o')
                    Send-Notice "Screen time for today is used up. Locking the PC until $($st.ResetHour):00." $true
                }
            }
        }
    }

    Save-Usage $u
}
catch {
    Write-NGLog "screen time tracker error: $($_.Exception.Message)"
}
