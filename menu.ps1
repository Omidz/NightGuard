# NightGuard - text menu. Start it with NightGuard.cmd or Start Menu > NightGuard.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

# Remove the 'downloaded from the internet' mark so SmartScreen stops blocking these files
Get-ChildItem -Path $PSScriptRoot -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

$me = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $me.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    exit
}

$Host.UI.RawUI.WindowTitle = 'NightGuard'
$SLDir = Join-Path $env:ProgramData 'ScreenLimits'

# ============================================================================
# Small input helpers
# ============================================================================
function Pause-Menu { Write-Host ''; Read-Host '   Press Enter to go back' | Out-Null }
function Say([string]$Text, [string]$Color = 'Gray') { Write-Host "   $Text" -ForegroundColor $Color }

function Ask([string]$Prompt, [string]$Default) {
    $a = Read-Host "   $Prompt [$Default]"
    if ([string]::IsNullOrWhiteSpace($a)) { return $Default }
    return $a.Trim()
}

function Ask-Time([string]$Prompt, [string]$Default) {
    while ($true) {
        $a = Ask $Prompt $Default
        if ($a -match $NGTimeRegex) { return ([timespan]::Parse($a)).ToString('hh\:mm') }
        Say 'Please type a time like 23:30.' Yellow
    }
}

function Ask-Int([string]$Prompt, [int]$Default, [int]$Min, [int]$Max) {
    while ($true) {
        $a = Ask "$Prompt ($Min-$Max)" ([string]$Default)
        $n = 0
        if ([int]::TryParse($a, [ref]$n) -and $n -ge $Min -and $n -le $Max) { return $n }
        Say "Please type a whole number from $Min to $Max." Yellow
    }
}

function Ask-YesNo([string]$Prompt, [bool]$Default) {
    $d = if ($Default) { 'y' } else { 'n' }
    while ($true) {
        $a = Ask "$Prompt (y/n)" $d
        if ($a -match '^[yY]') { return $true }
        if ($a -match '^[nN]') { return $false }
    }
}

function Ask-Days([string]$Prompt) {
    Say 'Nights: Mon Tue Wed Thu Fri Sat Sun (the evening the night starts).'
    Say 'Type days separated by spaces, or: all, weeknights (Sun-Thu), weekend (Fri Sat).'
    while ($true) {
        $a = (Read-Host "   $Prompt").Trim()
        switch -Regex ($a) {
            '^all$'        { return $NGDays }
            '^weeknights?$' { return @('Sun', 'Mon', 'Tue', 'Wed', 'Thu') }
            '^weekends?$'  { return @('Fri', 'Sat') }
        }
        $days = @($a -split '[\s,]+' | Where-Object { $_ } | ForEach-Object {
            $k = $_.Substring(0, [math]::Min(3, $_.Length))
            $NGDays | Where-Object { $_ -eq $k }
        })
        if ($days.Count -gt 0) { return $days }
        Say 'Please type days like: Fri Sat' Yellow
    }
}

# ============================================================================
# Status
# ============================================================================
function Get-NightText($Night) {
    if (-not $Night) { return 'none scheduled' }
    return ('{0:ddd HH\:mm} until {1:ddd HH\:mm}' -f $Night.Start, $Night.End)
}

function Show-Header {
    Clear-Host
    Write-Host ''
    Write-Host '   ===========================================' -ForegroundColor DarkCyan
    Write-Host '                   N I G H T G U A R D' -ForegroundColor Cyan
    Write-Host '   ===========================================' -ForegroundColor DarkCyan
    Write-Host ''
    $cfg = Get-NGConfig
    if (-not $cfg) {
        Say 'Status     : NOT INSTALLED' Yellow
    } else {
        Say 'Status     : ACTIVE' Green
        $cur = Get-NGCurrentNight $cfg
        if ($cur) { Say ("Locked now : until {0:ddd HH\:mm}" -f $cur.End) Magenta }
        Say ("Next night : {0}" -f (Get-NightText (Get-NGNextNight $cfg)))
        Say ("At bedtime : {0} (after a {1}s countdown)" -f $cfg.Action, $cfg.CountdownSeconds)
        $exp = Get-NGExpiry $cfg
        if ($exp) {
            Say ("End date   : last night {0}, removes itself {1:ddd yyyy-MM-dd HH\:mm}" -f $cfg.EndDate, $exp)
        } else {
            Say 'End date   : none (runs until you uninstall)'
        }
        if (Get-NGBlockedReason $cfg) { Say 'Night mode : ON, settings and uninstall are locked right now' Magenta }
        if (Get-NGTrusted) { Say 'Trusted    : ON, changes need the trusted person''s code' Cyan }
        if ($cfg.Blocking.Enabled) {
            $bs = if (Test-NGBlockActive $cfg) { 'blocking NOW' } else { 'on schedule' }
            Say ("Blocking   : {0} sites, {1} apps/folders ({2})" -f @($cfg.Blocking.Sites).Count, @($cfg.Blocking.Apps).Count, $bs)
        }
    }
    if (Test-Path (Join-Path $SLDir 'config.json')) { Say 'Screen limits tool: installed' DarkGray }
    Write-Host ''
}

function Show-Schedule($Cfg) {
    Say 'Night   On    Bedtime   Unlock' DarkGray
    foreach ($i in 0..6) {
        $d = $NGDays[$i]
        $s = $Cfg.Schedule.$d
        $on = if ($s.Enabled) { 'yes' } else { 'off' }
        $note = ''
        if ($s.Enabled -and [timespan]::Parse($s.Bedtime).TotalHours -lt 12) {
            $note = '(after midnight, ' + $NGDays[($i + 1) % 7] + ')'
        }
        $color = if ($s.Enabled) { 'Gray' } else { 'DarkGray' }
        Say ('{0,-7} {1,-5} {2,-9} {3,-7} {4}' -f $d, $on, $s.Bedtime, $s.Unlock, $note) $color
    }
}

function Show-Warnings($Cfg) {
    $ws = @($Cfg.Warnings | Where-Object { $_ })
    if ($ws.Count -eq 0) { Say 'No warnings.' DarkGray; return }
    $i = 1
    foreach ($w in $ws) {
        $snd = if ($w.Sound) { 'sound' } else { 'silent' }
        $preview = ([string]$w.Message).Replace('\n', ' ')
        if ($preview.Length -gt 50) { $preview = $preview.Substring(0, 47) + '...' }
        Say ('{0}. {1,3} min before, {2,-6}  "{3}"' -f $i, $w.MinutesBefore, $snd, $preview)
        $i++
    }
}

function Show-Details {
    Show-Header
    $cfg = Get-NGConfig
    if ($cfg) {
        Show-Schedule $cfg
        Write-Host ''
        Show-Warnings $cfg
        Write-Host ''
        Say ("Repeats every {0} min during the night. Night mode starts {1} min before bedtime." -f $cfg.RecheckMinutes, $cfg.NightModeMinutes)
        Write-Host ''
    }
    $tasks = Get-ScheduledTask -TaskPath $NGTaskPath -ErrorAction SilentlyContinue
    if (-not $tasks) { Say 'No scheduled tasks found.' Yellow }
    foreach ($t in $tasks) {
        $info = $t | Get-ScheduledTaskInfo
        Say ("{0,-20} {1,-8} next run: {2}" -f $t.TaskName, $t.State, $info.NextRunTime)
    }
    $log = Join-Path $NGDir 'log.txt'
    Write-Host ''
    if (Test-Path $log) {
        Say 'Recent activity:'
        Get-Content $log -Tail 8 | ForEach-Object { Say "  $_" DarkGray }
    } else {
        Say 'Nothing logged yet.'
    }
    Pause-Menu
}

# ============================================================================
# Settings editor (works on a copy; nothing changes until Save)
# ============================================================================
function Edit-Schedule($Cfg) {
    while ($true) {
        Clear-Host; Write-Host ''; Say 'SCHEDULE' Cyan; Write-Host ''
        Show-Schedule $Cfg
        Write-Host ''
        Say 'A bedtime before 12:00 counts as after midnight (Mon 00:30 = early Tuesday).' DarkGray
        Write-Host ''
        Say '[1] Set bedtime and unlock for some nights'
        Say '[2] Turn nights on or off'
        Say '[0] Back'
        Write-Host ''
        switch (Read-Host '   Choose') {
            '1' {
                $days = Ask-Days 'Which nights?'
                $first = $Cfg.Schedule.($days[0])
                $bt = Ask-Time 'Bedtime' $first.Bedtime
                $ut = Ask-Time 'Unlock ' $first.Unlock
                foreach ($d in $days) { $Cfg.Schedule.$d.Bedtime = $bt; $Cfg.Schedule.$d.Unlock = $ut; $Cfg.Schedule.$d.Enabled = $true }
            }
            '2' {
                $days = Ask-Days 'Which nights?'
                $on = Ask-YesNo 'Locked on these nights?' $true
                foreach ($d in $days) { $Cfg.Schedule.$d.Enabled = $on }
            }
            '0' { return }
        }
    }
}

function Read-WarningFields($W) {
    $W.MinutesBefore = Ask-Int 'Minutes before bedtime' ([int]$W.MinutesBefore) 1 360
    $W.Sound = Ask-YesNo 'Play a sound' ([bool]$W.Sound)
    Say 'Message. You can use {minutes} {bedtime} {unlock} {action}, and \n for a new line.' DarkGray
    Say "Current: $($W.Message)" DarkGray
    $m = Read-Host '   New message (Enter keeps it)'
    if (-not [string]::IsNullOrWhiteSpace($m)) { $W.Message = $m.Trim() }
}

function Edit-Warnings($Cfg) {
    while ($true) {
        Clear-Host; Write-Host ''; Say 'WARNINGS' Cyan; Write-Host ''
        Show-Warnings $Cfg
        Write-Host ''
        Say '[A] Add   [E] Edit   [R] Remove   [P] Preview   [0] Back'
        Write-Host ''
        $ws = @($Cfg.Warnings | Where-Object { $_ })
        switch (Read-Host '   Choose') {
            { $_ -match '^[aA]$' } {
                if ($ws.Count -ge 6) { Say 'You can have at most 6 warnings.' Yellow; Pause-Menu; break }
                $w = [pscustomobject]@{ MinutesBefore = 15; Sound = $true; Message = 'Bedtime in {minutes} minutes. The PC will {action} at {bedtime}.' }
                Read-WarningFields $w
                $Cfg.Warnings = @($ws + $w | Sort-Object { [int]$_.MinutesBefore } -Descending)
            }
            { $_ -match '^[eE]$' } {
                if ($ws.Count -eq 0) { break }
                $n = Ask-Int 'Which warning' 1 1 $ws.Count
                Read-WarningFields $ws[$n - 1]
                $Cfg.Warnings = @($ws | Sort-Object { [int]$_.MinutesBefore } -Descending)
            }
            { $_ -match '^[rR]$' } {
                if ($ws.Count -eq 0) { break }
                $n = Ask-Int 'Remove which warning' 1 1 $ws.Count
                $keep = @()
                for ($i = 0; $i -lt $ws.Count; $i++) { if ($i -ne $n - 1) { $keep += $ws[$i] } }
                $Cfg.Warnings = $keep
            }
            { $_ -match '^[pP]$' } {
                if ($ws.Count -eq 0) { break }
                $n = Ask-Int 'Preview which warning' 1 1 $ws.Count
                $tmp = Join-Path $env:TEMP 'nightguard-preview'
                New-Item -ItemType Directory -Force -Path $tmp | Out-Null
                Copy-Item (Join-Path $PSScriptRoot 'common.ps1'), (Join-Path $PSScriptRoot 'warn.ps1') -Destination $tmp -Force
                Set-Content -Path (Join-Path $tmp 'config.json') -Value (ConvertTo-NGJson $Cfg) -Encoding UTF8
                & (Join-Path $tmp 'warn.ps1') -Preview -Index $n
            }
            '0' { return }
        }
    }
}

function Edit-Action($Cfg) {
    Clear-Host; Write-Host ''; Say 'WHAT HAPPENS AT BEDTIME' Cyan; Write-Host ''
    Say '[1] Shutdown   - force shut down (strongest)'
    Say '[2] Sleep      - put the PC to sleep'
    Say '[3] Hibernate  - hibernate (falls back to shutdown if hibernate is off)'
    Say '[4] Lock       - lock the screen (weakest: you can sign back in)'
    Say '[5] SignOut    - force sign out, closing all apps'
    Write-Host ''
    $cur = [array]::IndexOf($NGActions, [string]$Cfg.Action) + 1
    $Cfg.Action = $NGActions[(Ask-Int 'Action' $cur 1 5) - 1]
    $Cfg.CountdownSeconds = Ask-Int 'Countdown before it happens, seconds' ([int]$Cfg.CountdownSeconds) 15 600
    Say 'During the night it repeats the action, so turning the PC back on does not help.' DarkGray
    $Cfg.RecheckMinutes = Ask-Int 'Repeat every N minutes' ([int]$Cfg.RecheckMinutes) 1 60
}

function Edit-NightMode($Cfg) {
    Clear-Host; Write-Host ''; Say 'NIGHT MODE' Cyan; Write-Host ''
    Say 'Settings and uninstall are locked from this many minutes before bedtime'
    Say 'until the unlock time, so you cannot back out in the moment.'
    Write-Host ''
    $Cfg.NightModeMinutes = Ask-Int 'Minutes before bedtime' ([int]$Cfg.NightModeMinutes) 0 240
}

function Edit-EndDate($Cfg) {
    Clear-Host; Write-Host ''; Say 'END DATE' Cyan; Write-Host ''
    Say 'The end date is the last night that is locked. After that night, NightGuard removes itself.'
    Say 'Type a date (2027-01-31), a number of days from today (+30), or none.'
    Write-Host ''
    $cur = if ($Cfg.EndDate) { [string]$Cfg.EndDate } else { 'none' }
    while ($true) {
        $e = Ask 'End date' $cur
        if ($e -eq 'none') { $Cfg.EndDate = $null; return }
        if ($e -match '^\+(\d{1,4})$') { $Cfg.EndDate = (Get-Date).Date.AddDays([int]$Matches[1]).ToString('yyyy-MM-dd'); return }
        $d = [datetime]::MinValue
        if ([datetime]::TryParseExact($e, 'yyyy-MM-dd', $NGInv, 'None', [ref]$d) -and $d -ge (Get-Date).Date) {
            $Cfg.EndDate = $d.ToString('yyyy-MM-dd'); return
        }
        Say 'Please type a future date like 2027-01-31, +30, or none.' Yellow
    }
}

function Edit-Advanced($Cfg) {
    $tmp = Join-Path $env:TEMP 'nightguard-settings.json'
    Set-Content -Path $tmp -Value (ConvertTo-NGJson $Cfg) -Encoding UTF8
    Say 'Opening the settings file in Notepad. Save and close Notepad to come back.'
    Say 'The top of common.ps1 explains every setting.' DarkGray
    Start-Process notepad.exe -ArgumentList "`"$tmp`"" -Wait
    try {
        $new = Read-NGConfigFile $tmp
        $errs = Test-NGConfig $new
        if ($errs.Count -gt 0) {
            Say 'That file has problems, so it was not used:' Red
            $errs | ForEach-Object { Say "  - $_" Red }
            Pause-Menu
            return $Cfg
        }
        return $new
    } catch {
        Say "Could not read that file: $($_.Exception.Message)" Red
        Pause-Menu
        return $Cfg
    }
}

function Edit-Settings {
    $installed = Get-NGConfig
    $blocked = Get-NGBlockedReason $installed
    if ($blocked) { Show-Header; Say $blocked Red; Pause-Menu; return }

    $cfg = if ($installed) { Copy-NGConfig $installed } else { Copy-NGConfig (New-NGDefaultConfig) }
    while ($true) {
        Clear-Host
        Write-Host ''
        Say 'SETTINGS (nothing changes until you choose Save)' Cyan
        Write-Host ''
        Show-Schedule $cfg
        Write-Host ''
        Show-Warnings $cfg
        Write-Host ''
        Say ("At bedtime: {0}, {1}s countdown, repeats every {2} min" -f $cfg.Action, $cfg.CountdownSeconds, $cfg.RecheckMinutes)
        Say ("Night mode: from {0} min before bedtime" -f $cfg.NightModeMinutes)
        Say ("End date  : {0}" -f $(if ($cfg.EndDate) { "last night $($cfg.EndDate)" } else { 'none' }))
        Write-Host ''
        Say '[1] Schedule (bedtime and unlock per night)'
        Say '[2] Warnings (when, message, sound)'
        Say '[3] What happens at bedtime (action, countdown, repeat)'
        Say '[4] Night mode'
        Say '[5] End date'
        Say '[6] Advanced: edit the settings file in Notepad'
        Say '[7] Reset to defaults'
        Write-Host ''
        Say '[S] Save and apply     [X] Discard changes' Green
        Write-Host ''
        switch (Read-Host '   Choose') {
            '1' { Edit-Schedule $cfg }
            '2' { Edit-Warnings $cfg }
            '3' { Edit-Action $cfg }
            '4' { Edit-NightMode $cfg }
            '5' { Edit-EndDate $cfg }
            '6' { $cfg = Edit-Advanced $cfg }
            '7' { if (Ask-YesNo 'Reset all settings to the defaults' $false) { $cfg = Copy-NGConfig (New-NGDefaultConfig) } }
            { $_ -match '^[sS]$' } {
                $errs = Test-NGConfig $cfg
                if ($errs.Count -gt 0) {
                    Say 'Fix these first:' Red
                    $errs | ForEach-Object { Say "  - $_" Red }
                    Pause-Menu
                    break
                }
                $tmp = Join-Path $env:TEMP 'nightguard-settings.json'
                Set-Content -Path $tmp -Value (ConvertTo-NGJson $cfg) -Encoding UTF8
                $code = ''
                if (Get-NGTrusted) { $code = Read-Host "   Trusted person's code (8 digits)" }
                & (Join-Path $PSScriptRoot 'install.ps1') -ConfigPath $tmp -Code $code -NoPause
                Remove-Item $tmp -Force -ErrorAction SilentlyContinue
                Pause-Menu
                return
            }
            { $_ -match '^[xX]$' } { return }
        }
    }
}

# ============================================================================
# Other actions
# ============================================================================
function Preview-Popups {
    Show-Header
    Say '[1] Warning popup   [2] Bedtime countdown (10 seconds, nothing happens after)'
    switch (Read-Host '   Choose') {
        '1' { & (Join-Path $PSScriptRoot 'warn.ps1') -Preview -Index 1 }
        '2' { & (Join-Path $PSScriptRoot 'session.ps1') -Preview }
    }
}

function Invoke-Uninstall {
    Show-Header
    if ((Read-Host '   Really remove NightGuard? (type YES)') -cne 'YES') { return }
    $code = ''
    if (Get-NGTrusted) { $code = Read-Host "   Trusted person's code (8 digits)" }
    & (Join-Path $PSScriptRoot 'uninstall.ps1') -Code $code -NoPause
    Pause-Menu
    # The installed copy of this menu is gone after an uninstall
    if (-not (Get-NGConfig) -and -not (Test-Path $PSCommandPath)) { exit }
}

function Show-SLUsage {
    $script = Join-Path $SLDir 'Show-ScreenLimits.ps1'
    Clear-Host
    if (Test-Path $script) { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script }
    else { Say 'The screen limits tool is not installed.' Yellow }
    Pause-Menu
}

function Edit-SLLimits {
    $cfgFile = Join-Path $SLDir 'config.json'
    if (Test-Path $cfgFile) {
        Say 'Opening the screen limits settings in Notepad. Changes apply within a minute after you save.'
        Start-Process notepad.exe -ArgumentList "`"$cfgFile`""
        Start-Sleep -Seconds 2
    } else {
        Say 'The screen limits tool is not installed.' Yellow
        Pause-Menu
    }
}

# ============================================================================
# Main menu
# ============================================================================
while ($true) {
    try {
        Show-Header
        $installed = [bool](Get-NGConfig)
        Say ('[1]  {0}' -f $(if ($installed) { 'Settings' } else { 'Install (review settings first)' }))
        Say '[2]  Details (schedule, tasks, log)'
        Say '[3]  Preview popups'
        Say '[4]  Uninstall'
        if (Test-Path (Join-Path $SLDir 'config.json')) {
            Say '[5]  Screen limits: today''s usage'
            Say '[6]  Screen limits: edit limits'
        }
        Say '[7]  Open the windowed app (blocking and trusted person are set up there)'
        Say '[0]  Exit'
        Write-Host ''
        switch (Read-Host '   Choose') {
            '1' { Edit-Settings }
            '2' { Show-Details }
            '3' { Preview-Popups }
            '4' { Invoke-Uninstall }
            '5' { Show-SLUsage }
            '6' { Edit-SLLimits }
            '7' { Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$(Join-Path $PSScriptRoot 'gui.ps1')`""); exit }
            '0' { exit }
        }
    } catch {
        Write-Host ''
        Say "Error: $($_.Exception.Message)" Red
        Pause-Menu
    }
}
