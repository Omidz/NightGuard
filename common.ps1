# NightGuard - shared helpers. Dot-sourced by the other scripts.
#
# Settings live in C:\ProgramData\NightGuard\config.json:
#   Schedule          Per night (Mon..Sun = the evening the night starts):
#                     Enabled, Bedtime (HH:mm), Unlock (HH:mm).
#                     A bedtime before 12:00 means after midnight, e.g. Mon 00:30 = Tuesday 00:30.
#   Warnings          List of { MinutesBefore, Sound, Message }. Message placeholders:
#                     {minutes} {bedtime} {unlock} {action}. Type \n for a new line.
#   Action            Shutdown | Sleep | Hibernate | Lock | SignOut
#   CountdownSeconds  Notice before the action (15-600)
#   RecheckMinutes    How often the action repeats during the night (1-60)
#   NightModeMinutes  Settings/uninstall are blocked from this many minutes before bedtime until unlock (0-240)
#   EndDate           yyyy-MM-dd: last night that is locked. The lock removes itself after it. null = never.
#   Why               Your reason, in your words (shown in the final warning, countdown and check-in)
#   ShortcutStartMenu / ShortcutDesktop   Where to put shortcuts to the app
#   MorningCheckIn    true/false: a cheerful popup on the first sign-in each day, with an optional mood + note journal
#   Blocking          { Enabled, DuringBedtime, Sites[], Apps[], Windows[ {Days[], From, To} ] }
#                     Sites are blocked in the hosts file, Apps (exe files or folders) with Windows Firewall.
#   ScreenTime        Daily game and screen time budgets:
#                     { Enabled, ResetHour (0-23, the hour a new day starts),
#                       GameMinutesWeekday, GameMinutesWeekend (0 = no limit, else 5-1440),
#                       ScreenMinutesWeekday, ScreenMinutesWeekend (0 = no limit, else 30-1440),
#                       WarnAtMinutesLeft[] (1-120), LimitAction (Lock | Shutdown | Warn),
#                       ShutdownGraceMinutes (1-10), CloseLaunchers,
#                       GameFolders[] (anything running from these counts as a game),
#                       GameProcesses[], IgnoreProcesses[], LauncherProcesses[] (names without .exe) }
#
# Trusted-person mode keeps a salted hash of the 8-digit code in trusted.json (admins only).
# There is no recovery: without the code, settings and uninstall stay locked.

# The app's name. Everything Windows-side (folder, tasks, firewall group, hosts markers,
# Start Menu entries) is derived from it, so renaming means changing this line
# (plus user-facing text and the .cmd file names).
$NGAppName   = 'NightGuard'

$NGDir       = Join-Path $env:ProgramData $NGAppName
$NGTaskPath  = "\$NGAppName\"
$NGStartMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
$NGShortcut  = Join-Path $NGStartMenu "$NGAppName.lnk"
$NGShortcut2 = Join-Path $NGStartMenu "$NGAppName (text menu).lnk"
$NGShortcutDesk = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) "$NGAppName.lnk"
$NGDays      = @('Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun')
$NGActions   = @('Shutdown', 'Sleep', 'Hibernate', 'Lock', 'SignOut')
$NGActionWords = @{ Shutdown = 'shut down'; Sleep = 'go to sleep'; Hibernate = 'hibernate'; Lock = 'lock'; SignOut = 'sign you out' }
$NGTimeRegex = '^([01]?\d|2[0-3]):[0-5]\d$'
$NGInv       = [Globalization.CultureInfo]::InvariantCulture
$NGHosts     = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$NGHostsBegin = "# >>> $NGAppName BEGIN - managed by $NGAppName, removed automatically"
$NGHostsEnd   = "# <<< $NGAppName END"
$NGFwGroup   = $NGAppName
$NGScripts   = @('common.ps1', 'enforce.ps1', 'session.ps1', 'warn.ps1', 'blocker.ps1', 'tracker.ps1', 'notify.ps1', 'checkin.ps1', 'install.ps1', 'uninstall.ps1', 'menu.ps1', 'gui.ps1', "$NGAppName.cmd", 'TextMenu.cmd')
# Earlier name of this tool; old installs are migrated and removed on install
$NGLegacyName = 'BedtimeLock'
$NGLegacyDir  = Join-Path $env:ProgramData $NGLegacyName
# The separate Screen Limits add-on, now built in; old installs are migrated and removed
$NGOldSLName  = 'ScreenLimits'
$NGOldSLDir   = Join-Path $env:ProgramData $NGOldSLName
$NGScreenActions = @('Lock', 'Shutdown', 'Warn')
$NGProcRegex  = '^[\w .+()\-]{1,100}$'
$NGSocialPreset = @('instagram.com', 'facebook.com', 'tiktok.com', 'x.com', 'twitter.com', 'reddit.com', 'youtube.com', 'snapchat.com', 'twitch.tv', 'pinterest.com')
# Never block these: Windows, updates, security and sign-in would break
$NGProtectedDomains = @('microsoft.com', 'windows.com', 'windowsupdate.com', 'live.com', 'microsoftonline.com', 'msftconnecttest.com',
                        'msftncsi.com', 'office.com', 'office.net', 'azure.com', 'digicert.com', 'verisign.com', 'globalsign.com',
                        'letsencrypt.org', 'localhost', 'anthropic.com', 'claude.ai')

function Write-NGLog([string]$Text) {
    try {
        $log = Join-Path $NGDir 'log.txt'
        Add-Content -Path $log -Value ("{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Text)
        $lines = Get-Content -Path $log
        if ($lines.Count -gt 500) { $lines | Select-Object -Last 300 | Set-Content -Path $log }
    } catch { }
}

function Test-NGSafeMode {
    try { return ((Get-CimInstance Win32_ComputerSystem).BootupState -ne 'Normal boot') } catch { return $false }
}

function Get-NGDayKey([datetime]$Date) { return $NGDays[(([int]$Date.DayOfWeek) + 6) % 7] }

# ============================================================================
# Config
# ============================================================================
function New-NGDefaultBlocking {
    return [pscustomobject]@{ Enabled = $false; DuringBedtime = $true; Sites = $NGSocialPreset; Apps = @(); Windows = @() }
}

function New-NGDefaultScreenTime {
    return [pscustomobject]@{
        Enabled = $false; ResetHour = 4
        GameMinutesWeekday = 60; GameMinutesWeekend = 120
        ScreenMinutesWeekday = 240; ScreenMinutesWeekend = 360
        WarnAtMinutesLeft = @(15, 5, 1); LimitAction = 'Lock'; ShutdownGraceMinutes = 2; CloseLaunchers = $true
        GameFolders = @(
            'C:\Program Files (x86)\Steam\steamapps\common', 'C:\Program Files\Steam\steamapps\common',
            'D:\SteamLibrary\steamapps\common', 'E:\SteamLibrary\steamapps\common',
            'C:\Program Files\Epic Games', 'D:\Epic Games', 'C:\XboxGames', 'D:\XboxGames',
            'C:\Program Files (x86)\GOG Galaxy\Games', 'C:\GOG Games', 'C:\Riot Games',
            'C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games', 'C:\Program Files\EA Games',
            'C:\Program Files (x86)\Battle.net\Games')
        GameProcesses = @('League of Legends', 'VALORANT-Win64-Shipping', 'FortniteClient-Win64-Shipping', 'cs2', 'dota2',
            'RobloxPlayerBeta', 'Minecraft.Windows', 'javaw', 'GTA5', 'RocketLeague', 'r5apex', 'Overwatch', 'eldenring')
        IgnoreProcesses = @('steam', 'steamwebhelper', 'steamservice', 'EpicGamesLauncher', 'EpicWebHelper', 'UnrealCEFSubProcess',
            'CrashReportClient', 'UnityCrashHandler64', 'UnityCrashHandler32', 'GameBar', 'GameBarFTServer', 'XboxPcApp',
            'WindowsTerminal', 'Microsoft.Photos', 'Calculator', 'CalculatorApp', 'Notepad', 'msedge', 'Spotify', 'WhatsApp', 'Teams', 'ms-teams')
        LauncherProcesses = @('steam', 'steamwebhelper', 'EpicGamesLauncher', 'EpicWebHelper', 'GalaxyClient', 'Battle.net',
            'EADesktop', 'upc', 'RiotClientServices', 'XboxPcApp')
    }
}

# Turns an old Screen Limits config.json into NightGuard's ScreenTime section (turned on).
function Convert-NGOldScreenLimits($Old) {
    $st = New-NGDefaultScreenTime
    foreach ($k in 'ResetHour', 'GameMinutesWeekday', 'GameMinutesWeekend', 'ScreenMinutesWeekday', 'ScreenMinutesWeekend',
                   'WarnAtMinutesLeft', 'ShutdownGraceMinutes', 'GameFolders', 'GameProcesses', 'IgnoreProcesses', 'LauncherProcesses') {
        if ($null -ne $Old.$k) { $st.$k = $Old.$k }
    }
    if ($Old.ScreenLimitAction) { $st.LimitAction = [string]$Old.ScreenLimitAction }
    if ($null -ne $Old.CloseLaunchersWhenGameLimitHit) { $st.CloseLaunchers = [bool]$Old.CloseLaunchersWhenGameLimitHit }
    $st.Enabled = $true
    return $st
}

# If the old Screen Limits add-on is installed, returns its settings as a ScreenTime section.
function Get-NGOldScreenLimits {
    $f = Join-Path $NGOldSLDir 'config.json'
    if (Test-Path $f) { try { return Convert-NGOldScreenLimits (Get-Content $f -Raw | ConvertFrom-Json) } catch { } }
    return $null
}

function Remove-NGOldScreenLimits {
    Get-ScheduledTask -TaskPath "\$NGOldSLName\" -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
    try {
        $svc = New-Object -ComObject 'Schedule.Service'
        $svc.Connect()
        $svc.GetFolder('\').DeleteFolder($NGOldSLName, 0)
    } catch { }
    Remove-Item -Path $NGOldSLDir -Recurse -Force -ErrorAction SilentlyContinue
}

function New-NGDefaultConfig {
    $sched = [ordered]@{}
    foreach ($d in $NGDays) { $sched[$d] = [pscustomobject]@{ Enabled = $true; Bedtime = '00:00'; Unlock = '06:00' } }
    $cfg = [pscustomobject]@{
        Version          = 3
        Schedule         = [pscustomobject]$sched
        Warnings         = @(
            [pscustomobject]@{ MinutesBefore = 30; Sound = $true; Message = 'Bedtime in {minutes} minutes.\n\nThe PC will {action} at {bedtime} and stays locked until {unlock}.\n\nFinish your match, save your work, and start winding down.' },
            [pscustomobject]@{ MinutesBefore = 5;  Sound = $true; Message = 'FINAL WARNING\n\nThe PC will {action} at {bedtime} (about {minutes} min).\n\nSave your work and quit your game NOW.' }
        )
        Action           = 'Hibernate'
        CountdownSeconds = 60
        RecheckMinutes   = 1
        NightModeMinutes = 60
        EndDate          = $null
        MorningCheckIn   = $true
        Why              = ''
        ShortcutStartMenu = $true
        ShortcutDesktop  = $false
        Blocking         = New-NGDefaultBlocking
        ScreenTime       = New-NGDefaultScreenTime
    }
    return $cfg
}

function ConvertTo-NGStringArray($Items) {
    $list = New-Object System.Collections.ArrayList
    foreach ($x in @($Items)) { if ($null -ne $x -and "$x".Trim()) { [void]$list.Add(([string]$x).Trim()) } }
    return , $list
}

# Turns a config into clean JSON (works around PowerShell 5.1 array quirks).
function ConvertTo-NGJson($Cfg) {
    $sched = [ordered]@{}
    foreach ($d in $NGDays) {
        $s = $Cfg.Schedule.$d
        $sched[$d] = [ordered]@{ Enabled = [bool]$s.Enabled; Bedtime = [string]$s.Bedtime; Unlock = [string]$s.Unlock }
    }
    $warn = New-Object System.Collections.ArrayList
    foreach ($w in (@($Cfg.Warnings) | Where-Object { $_ } | Sort-Object { [int]$_.MinutesBefore } -Descending)) {
        [void]$warn.Add([ordered]@{ MinutesBefore = [int]$w.MinutesBefore; Sound = [bool]$w.Sound; Message = [string]$w.Message })
    }
    $b = $Cfg.Blocking
    if (-not $b) { $b = New-NGDefaultBlocking }
    $wins = New-Object System.Collections.ArrayList
    foreach ($w in @($b.Windows | Where-Object { $_ })) {
        [void]$wins.Add([ordered]@{ Days = (ConvertTo-NGStringArray $w.Days); From = [string]$w.From; To = [string]$w.To })
    }
    $obj = [ordered]@{
        Version          = 3
        Schedule         = $sched
        Warnings         = $warn
        Action           = [string]$Cfg.Action
        CountdownSeconds = [int]$Cfg.CountdownSeconds
        RecheckMinutes   = [int]$Cfg.RecheckMinutes
        NightModeMinutes = [int]$Cfg.NightModeMinutes
        EndDate          = $(if ($Cfg.EndDate) { [string]$Cfg.EndDate } else { $null })
        MorningCheckIn   = $(if ($null -eq $Cfg.MorningCheckIn) { $true } else { [bool]$Cfg.MorningCheckIn })
        Why              = $(if ($Cfg.Why) { ([string]$Cfg.Why).Trim() } else { '' })
        ShortcutStartMenu = $(if ($null -eq $Cfg.ShortcutStartMenu) { $true } else { [bool]$Cfg.ShortcutStartMenu })
        ShortcutDesktop  = [bool]$Cfg.ShortcutDesktop
        Blocking         = [ordered]@{
            Enabled       = [bool]$b.Enabled
            DuringBedtime = [bool]$b.DuringBedtime
            Sites         = (ConvertTo-NGStringArray ($b.Sites | ForEach-Object { "$_".ToLowerInvariant() }))
            Apps          = (ConvertTo-NGStringArray $b.Apps)
            Windows       = $wins
        }
        ScreenTime       = $null
    }
    $st = $Cfg.ScreenTime
    if (-not $st) { $st = New-NGDefaultScreenTime }
    $warnAt = New-Object System.Collections.ArrayList
    foreach ($m in (@($st.WarnAtMinutesLeft) | Where-Object { $null -ne $_ -and "$_".Trim() } | ForEach-Object { [int]$_ } | Sort-Object -Descending -Unique)) { [void]$warnAt.Add($m) }
    $obj.ScreenTime = [ordered]@{
        Enabled              = [bool]$st.Enabled
        ResetHour            = [int]$st.ResetHour
        GameMinutesWeekday   = [int]$st.GameMinutesWeekday
        GameMinutesWeekend   = [int]$st.GameMinutesWeekend
        ScreenMinutesWeekday = [int]$st.ScreenMinutesWeekday
        ScreenMinutesWeekend = [int]$st.ScreenMinutesWeekend
        WarnAtMinutesLeft    = $warnAt
        LimitAction          = [string]$st.LimitAction
        ShutdownGraceMinutes = [int]$st.ShutdownGraceMinutes
        CloseLaunchers       = [bool]$st.CloseLaunchers
        GameFolders          = (ConvertTo-NGStringArray $st.GameFolders)
        GameProcesses        = (ConvertTo-NGStringArray ($st.GameProcesses | ForEach-Object { "$_" -replace '\.exe$', '' }))
        IgnoreProcesses      = (ConvertTo-NGStringArray ($st.IgnoreProcesses | ForEach-Object { "$_" -replace '\.exe$', '' }))
        LauncherProcesses    = (ConvertTo-NGStringArray ($st.LauncherProcesses | ForEach-Object { "$_" -replace '\.exe$', '' }))
    }
    return ($obj | ConvertTo-Json -Depth 8)
}

# Deep copy / normalise
function Copy-NGConfig($Cfg) { return (ConvertTo-NGJson $Cfg | ConvertFrom-Json) }

# Upgrades a version-1 config (single ShutdownTime/UnlockTime/WarnTimes).
function Convert-NGOldConfig($Old) {
    $cfg = New-NGDefaultConfig
    foreach ($d in $NGDays) { $cfg.Schedule.$d.Bedtime = $Old.ShutdownTime; $cfg.Schedule.$d.Unlock = $Old.UnlockTime }
    $bt = [timespan]::Parse($Old.ShutdownTime)
    $list = @()
    foreach ($t in @($Old.WarnTimes)) {
        $m = ($bt - [timespan]::Parse($t)).TotalMinutes
        if ($m -le 0) { $m += 1440 }
        $tmpl = if ($m -le 5) { $cfg.Warnings[1].Message } else { $cfg.Warnings[0].Message }
        $list += [pscustomobject]@{ MinutesBefore = [int]$m; Sound = $true; Message = $tmpl }
    }
    $cfg.Warnings = $list
    $cfg.EndDate = $Old.EndDate
    return (Copy-NGConfig $cfg)
}

function Read-NGConfigFile([string]$Path) {
    $raw = Get-Content -Path $Path -Raw | ConvertFrom-Json
    if (-not $raw.Version) { return Convert-NGOldConfig $raw }
    return (Copy-NGConfig $raw)   # also fills in Blocking for version 2
}

# Returns the installed settings, or $null if there are none or the file can't be read.
# Callers treat $null as "do nothing", so a damaged settings file never causes a shutdown.
function Get-NGConfig {
    $f = Join-Path $NGDir 'config.json'
    if (Test-Path $f) {
        try { return Read-NGConfigFile $f }
        catch { Write-NGLog "settings file can't be read, doing nothing: $($_.Exception.Message)"; return $null }
    }
    return $null
}

# Safety gate used by everything that enforces: settings must exist, be valid,
# and the clock must look right. Returns $null when it's safe to act, or the reason not to.
function Get-NGUnsafeReason($Cfg) {
    if (-not $Cfg) { return 'no settings' }
    $errs = Test-NGConfig $Cfg
    if ($errs.Count -gt 0) { return "settings are invalid: $($errs[0])" }
    # A clock far in the past (dead CMOS battery, BIOS reset) could otherwise
    # cause a shutdown at every boot. Settings saved "in the future" means the clock is wrong.
    $f = Join-Path $NGDir 'config.json'
    if (Test-Path $f) {
        $saved = (Get-Item $f).LastWriteTime
        if ((Get-Date) -lt $saved.AddDays(-1)) { return "the clock looks wrong ($(Get-Date -Format 'yyyy-MM-dd HH:mm') is before the settings were saved)" }
    }
    return $null
}

function Test-NGDomain([string]$D) {
    return ($D -match '^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$')
}

function Test-NGProtectedDomain([string]$D) {
    foreach ($p in $NGProtectedDomains) { if ($D -eq $p -or $D.EndsWith(".$p")) { return $true } }
    return $false
}

function Test-NGProtectedPath([string]$P) {
    $full = $P.TrimEnd('\')
    foreach ($bad in @($env:SystemRoot, (Join-Path $env:ProgramFiles 'Windows Defender'), (Join-Path $env:ProgramData 'Microsoft'), $NGDir)) {
        if ($full -eq $bad -or $full.StartsWith("$bad\", [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    if ($full.Length -le 3) { return $true }   # a whole drive
    # Whole top-level folders would catch far too much (browsers, drivers, Windows tools)
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramData, $env:USERPROFILE, $env:LOCALAPPDATA, $env:APPDATA, (Split-Path $env:USERPROFILE -Parent))) {
        if ($root -and $full -eq $root.TrimEnd('\')) { return $true }
    }
    return $false
}

# Returns a list of problems; empty means the config is fine.
function Test-NGConfig($Cfg) {
    $errs = @()
    $anyOn = $false
    foreach ($d in $NGDays) {
        $s = $Cfg.Schedule.$d
        if (-not $s) { $errs += "$d is missing from the schedule."; continue }
        if ($s.Bedtime -notmatch $NGTimeRegex) { $errs += "$d bedtime '$($s.Bedtime)' is not a time like 23:30." }
        if ($s.Unlock  -notmatch $NGTimeRegex) { $errs += "$d unlock time '$($s.Unlock)' is not a time like 06:00." }
        if ($s.Enabled) { $anyOn = $true }
    }
    if (-not $anyOn) { $errs += 'Every night is turned off. Turn on at least one night, or uninstall instead.' }
    if ($Cfg.ShortcutStartMenu -eq $false -and -not $Cfg.ShortcutDesktop) { $errs += 'Keep at least one shortcut (Start Menu or Desktop), so you can open NightGuard later.' }
    if (([string]$Cfg.Why).Length -gt 300) { $errs += '"Why I started" can be at most 300 characters.' }
    if ($errs.Count -eq 0) {
        $ref = [datetime]'2024-01-01'   # a Monday
        foreach ($i in 0..6) {
            $n = Get-NGNight $Cfg ($ref.AddDays($i)) -IgnoreEndDate
            $s = $Cfg.Schedule.($NGDays[$i])
            if ($s.Enabled -and -not $n) { $errs += "$($NGDays[$i]): unlock time $($s.Unlock) must be after bedtime $($s.Bedtime)."; continue }
            if (-not $n) { continue }
            if (($n.End - $n.Start).TotalHours -gt 16) { $errs += "$($NGDays[$i]): the locked time is longer than 16 hours." }
            # Safety: always leave at least 6 free hours before the next night starts
            $next = Get-NGNight $Cfg ($ref.AddDays($i + 1)) -IgnoreEndDate
            if ($next -and ($next.Start - $n.End).TotalHours -lt 6) {
                $errs += "$($NGDays[$i]) night ends at $($n.End.ToString('HH:mm')) but the next night starts at $($next.Start.ToString('HH:mm')). Leave at least 6 free hours."
            }
        }
    }
    $warn = @($Cfg.Warnings | Where-Object { $_ })
    if ($warn.Count -gt 6) { $errs += 'At most 6 warnings are allowed.' }
    foreach ($w in $warn) {
        if ([int]$w.MinutesBefore -lt 1 -or [int]$w.MinutesBefore -gt 360) { $errs += "Warning minutes must be between 1 and 360 (got $($w.MinutesBefore))." }
    }
    $dupes = $warn | Group-Object { [int]$_.MinutesBefore } | Where-Object { $_.Count -gt 1 }
    foreach ($g in $dupes) { $errs += "Two warnings are set for $($g.Name) minutes before bedtime." }
    if ($NGActions -notcontains $Cfg.Action) { $errs += "Action must be one of: $($NGActions -join ', ')." }
    if ([int]$Cfg.CountdownSeconds -lt 15 -or [int]$Cfg.CountdownSeconds -gt 600) { $errs += 'Countdown must be 15 to 600 seconds.' }
    if ([int]$Cfg.RecheckMinutes -lt 1 -or [int]$Cfg.RecheckMinutes -gt 60) { $errs += 'Repeat interval must be 1 to 60 minutes.' }
    if ([int]$Cfg.NightModeMinutes -lt 0 -or [int]$Cfg.NightModeMinutes -gt 240) { $errs += 'Night mode must start 0 to 240 minutes before bedtime.' }
    if ($Cfg.EndDate) {
        $d = [datetime]::MinValue
        if (-not [datetime]::TryParseExact([string]$Cfg.EndDate, 'yyyy-MM-dd', $NGInv, 'None', [ref]$d)) { $errs += "End date '$($Cfg.EndDate)' is not a date like 2027-01-31." }
    }

    $b = $Cfg.Blocking
    if ($b) {
        $sites = @($b.Sites | Where-Object { $_ })
        if ($sites.Count -gt 300) { $errs += 'At most 300 blocked sites are allowed.' }
        foreach ($s in $sites) {
            $s = "$s".ToLowerInvariant()
            if (-not (Test-NGDomain $s)) { $errs += "Blocked site '$s' is not a site name like instagram.com." }
            elseif (Test-NGProtectedDomain $s) { $errs += "'$s' can't be blocked, because Windows needs it." }
        }
        $apps = @($b.Apps | Where-Object { $_ })
        if ($apps.Count -gt 50) { $errs += 'At most 50 blocked apps or folders are allowed.' }
        foreach ($a in $apps) {
            if (-not [IO.Path]::IsPathRooted([string]$a)) { $errs += "Blocked app '$a' must be a full path like C:\Games\Steam." }
            elseif (Test-NGProtectedPath ([string]$a)) { $errs += "'$a' can't be blocked, because Windows needs it." }
        }
        $wins = @($b.Windows | Where-Object { $_ })
        if ($wins.Count -gt 20) { $errs += 'At most 20 blocking time slots are allowed.' }
        foreach ($w in $wins) {
            $days = @($w.Days | Where-Object { $_ })
            if ($days.Count -eq 0) { $errs += "A blocking time slot ($($w.From)-$($w.To)) has no days ticked." }
            foreach ($d in $days) { if ($NGDays -notcontains $d) { $errs += "Unknown day '$d' in a blocking time slot." } }
            if ($w.From -notmatch $NGTimeRegex -or $w.To -notmatch $NGTimeRegex) { $errs += "Blocking time slot '$($w.From)-$($w.To)' needs times like 09:00." }
            elseif ([timespan]::Parse($w.From) -eq [timespan]::Parse($w.To)) { $errs += "Blocking time slot $($w.From)-$($w.To) starts and ends at the same time." }
        }
    }

    $st = $Cfg.ScreenTime
    if ($st -and $st.Enabled) {
        if ([int]$st.ResetHour -lt 0 -or [int]$st.ResetHour -gt 23) { $errs += 'Screen time: the new day must start at an hour from 0 to 23.' }
        foreach ($k in 'GameMinutesWeekday', 'GameMinutesWeekend') {
            $v = [int]$st.$k
            if ($v -ne 0 -and ($v -lt 5 -or $v -gt 1440)) { $errs += "Screen time: game time must be 0 (no limit) or 5 to 1440 minutes (got $v)." }
        }
        foreach ($k in 'ScreenMinutesWeekday', 'ScreenMinutesWeekend') {
            # Safety: at least 30 minutes, so the PC is never unusable for a whole day
            $v = [int]$st.$k
            if ($v -ne 0 -and ($v -lt 30 -or $v -gt 1440)) { $errs += "Screen time: screen time must be 0 (no limit) or 30 to 1440 minutes (got $v)." }
        }
        $wa = @($st.WarnAtMinutesLeft | Where-Object { $null -ne $_ })
        if ($wa.Count -gt 6) { $errs += 'Screen time: at most 6 warnings.' }
        foreach ($m in $wa) { if ([int]$m -lt 1 -or [int]$m -gt 120) { $errs += "Screen time: warnings must be 1 to 120 minutes before the limit (got $m)." } }
        if ($NGScreenActions -notcontains [string]$st.LimitAction) { $errs += "Screen time: the action must be one of: $($NGScreenActions -join ', ')." }
        if ([int]$st.ShutdownGraceMinutes -lt 1 -or [int]$st.ShutdownGraceMinutes -gt 10) { $errs += 'Screen time: shutdown notice must be 1 to 10 minutes.' }
        $gf = @($st.GameFolders | Where-Object { $_ })
        if ($gf.Count -gt 50) { $errs += 'Screen time: at most 50 game folders.' }
        foreach ($f in $gf) {
            if (-not [IO.Path]::IsPathRooted([string]$f)) { $errs += "Screen time: game folder '$f' must be a full path." }
            elseif (Test-NGProtectedPath ([string]$f)) { $errs += "Screen time: '$f' can't be a game folder, because Windows runs from there." }
        }
        foreach ($k in 'GameProcesses', 'IgnoreProcesses', 'LauncherProcesses') {
            $list = @($st.$k | Where-Object { $_ })
            if ($list.Count -gt 200) { $errs += "Screen time: at most 200 entries in $k." }
            foreach ($n in $list) {
                if ("$n" -notmatch $NGProcRegex) { $errs += "Screen time: '$n' is not a program name (use the name from Task Manager, without .exe)." }
                elseif ($NGNeverKill -contains "$n".ToLowerInvariant() -and $k -ne 'IgnoreProcesses') { $errs += "Screen time: '$n' is part of Windows and can't be treated as a game or launcher." }
            }
        }
    }
    return , $errs
}

# ============================================================================
# Nights and time windows
# ============================================================================
function Get-NGEndDate($Cfg) {
    if ($Cfg -and $Cfg.EndDate) { return [datetime]::ParseExact([string]$Cfg.EndDate, 'yyyy-MM-dd', $NGInv) }
    return $null
}

# The night that starts on the evening of $Evening. $null if that night is off.
function Get-NGNight($Cfg, [datetime]$Evening, [switch]$IgnoreEndDate) {
    $key = Get-NGDayKey $Evening
    $s = $Cfg.Schedule.$key
    if (-not $s -or -not $s.Enabled) { return $null }
    if (-not $IgnoreEndDate) {
        $end = Get-NGEndDate $Cfg
        if ($end -and $Evening.Date -gt $end) { return $null }
    }
    $bt = [timespan]::Parse($s.Bedtime)
    $ut = [timespan]::Parse($s.Unlock)
    $start = $Evening.Date.Add($bt)
    if ($bt.TotalHours -lt 12) { $start = $start.AddDays(1) }   # after midnight
    $stop = $Evening.Date.AddDays(1).Add($ut)
    if ($stop -le $start) { return $null }
    return [pscustomobject]@{ Day = $key; Evening = $Evening.Date; Start = $start; End = $stop }
}

function Get-NGNights($Cfg, [datetime]$Now = (Get-Date)) {
    foreach ($i in -1..7) {
        $n = Get-NGNight $Cfg ($Now.Date.AddDays($i))
        if ($n) { $n }
    }
}

function Get-NGCurrentNight($Cfg, [datetime]$Now = (Get-Date)) {
    return (Get-NGNights $Cfg $Now | Where-Object { $Now -ge $_.Start -and $Now -lt $_.End } | Select-Object -First 1)
}

function Get-NGNextNight($Cfg, [datetime]$Now = (Get-Date)) {
    return (Get-NGNights $Cfg $Now | Where-Object { $_.Start -gt $Now } | Sort-Object Start | Select-Object -First 1)
}

# When the lock stops for good ($null = never).
function Get-NGExpiry($Cfg) {
    $end = Get-NGEndDate $Cfg
    if (-not $end) { return $null }
    $last = Get-NGNight $Cfg $end
    if ($last) { return $last.End }
    return $end.AddDays(1)
}

function Test-NGExpired($Cfg) {
    $x = Get-NGExpiry $Cfg
    return ($null -ne $x) -and ((Get-Date) -ge $x)
}

# Returns a message if settings changes / uninstall are blocked right now, else $null.
function Get-NGBlockedReason($Cfg) {
    if (-not $Cfg) { return $null }
    if (Test-NGExpired $Cfg) { return $null }
    $now = Get-Date
    foreach ($n in (Get-NGNights $Cfg $now)) {
        $from = $n.Start.AddMinutes(-[int]$Cfg.NightModeMinutes)
        if ($now -ge $from -and $now -lt $n.End) {
            return ("Night mode is on. Settings and uninstall are locked from {0:HH\:mm} until {1:ddd HH\:mm}. Go to bed, and decide tomorrow." -f $from, $n.End)
        }
    }
    return $null
}

function Format-NGMessage([string]$Text, $Cfg, $Night, [int]$Minutes) {
    $t = $Text.Replace('\n', "`n")
    $t = $t.Replace('{minutes}', [string]$Minutes)
    $t = $t.Replace('{bedtime}', $Night.Start.ToString('HH:mm'))
    $t = $t.Replace('{unlock}',  $Night.End.ToString('HH:mm'))
    $t = $t.Replace('{action}',  $NGActionWords[[string]$Cfg.Action])
    return $t
}

# Is site/app blocking active at $Now?
function Test-NGBlockActive($Cfg, [datetime]$Now = (Get-Date)) {
    $b = $Cfg.Blocking
    if (-not $b -or -not $b.Enabled) { return $false }
    if ($b.DuringBedtime -and (Get-NGCurrentNight $Cfg $Now)) { return $true }
    $today = Get-NGDayKey $Now
    $yday  = Get-NGDayKey ($Now.AddDays(-1))
    $t = $Now.TimeOfDay
    foreach ($w in @($b.Windows | Where-Object { $_ })) {
        $f  = [timespan]::Parse($w.From)
        $to = [timespan]::Parse($w.To)
        $days = @($w.Days)
        if ($f -lt $to) {
            if ($days -contains $today -and $t -ge $f -and $t -lt $to) { return $true }
        } else {
            # slot crosses midnight: belongs to the day it starts
            if (($days -contains $today -and $t -ge $f) -or ($days -contains $yday -and $t -lt $to)) { return $true }
        }
    }
    return $false
}

# ============================================================================
# Site blocking (hosts file) and app blocking (Windows Firewall)
# Only ever touches the marked hosts section and firewall rules in group "NightGuard".
# ============================================================================
function Get-NGHostsDomains($Cfg) {
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($s in @($Cfg.Blocking.Sites | Where-Object { $_ })) {
        $d = "$s".ToLowerInvariant().Trim()
        if (-not (Test-NGDomain $d) -or (Test-NGProtectedDomain $d)) { continue }
        $bare = $d -replace '^(www|m)\.', ''
        foreach ($v in @($bare, "www.$bare", "m.$bare", $d)) { if (-not $out.Contains($v)) { $out.Add($v) } }
    }
    return , $out.ToArray()
}

function Remove-NGHostsSection([string]$Text) {
    # Removes our marked section line by line. If the end marker is missing (hand-edited
    # file), only the "0.0.0.0" lines straight after the start marker are removed.
    $out = New-Object System.Collections.Generic.List[string]
    $inside = $false
    $lines = $Text -split "`r?`n"
    $hasEnd = @($lines | Where-Object { $_.StartsWith("# <<< $NGAppName END") }).Count -gt 0
    foreach ($l in $lines) {
        if ($l.StartsWith("# >>> $NGAppName BEGIN")) { $inside = $true; continue }
        if ($inside) {
            if ($l.StartsWith("# <<< $NGAppName END")) { $inside = $false; continue }
            if ($hasEnd -or $l -match '^0\.0\.0\.0\s') { continue }
            $inside = $false
        }
        $out.Add($l)
    }
    $r = ($out -join "`r`n")
    return $r
}

# Writes (or removes, when $Domains is empty) the NightGuard section. Returns $true if the file changed.
function Set-NGHostsBlock([string[]]$Domains) {
    $orig = ''
    if (Test-Path $NGHosts) { $orig = [IO.File]::ReadAllText($NGHosts) }
    $clean = Remove-NGHostsSection $orig
    if ($clean -ceq (($orig -split "`r?`n") -join "`r`n")) { $clean = $orig }   # untouched: keep exact bytes
    $new = $clean
    if ($Domains -and $Domains.Count -gt 0) {
        $new = $new.TrimEnd("`r", "`n") + "`r`n"
        if ($new -eq "`r`n") { $new = '' }
        $lines = @($NGHostsBegin) + ($Domains | ForEach-Object { "0.0.0.0 $_" }) + @($NGHostsEnd)
        $new += ($lines -join "`r`n") + "`r`n"
    }
    if ($new -ceq $orig) { return $false }

    $backup = Join-Path $NGDir 'hosts.backup'
    if ((Test-Path $NGHosts) -and -not (Test-Path $backup) -and (Test-Path $NGDir)) { Copy-Item $NGHosts $backup -Force }

    $item = Get-Item $NGHosts -ErrorAction SilentlyContinue
    $wasReadOnly = $item -and $item.IsReadOnly
    if ($wasReadOnly) { $item.IsReadOnly = $false }
    $ok = $false
    for ($try = 0; $try -lt 5 -and -not $ok; $try++) {
        try { [IO.File]::WriteAllText($NGHosts, $new, (New-Object Text.UTF8Encoding $false)); $ok = $true }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if ($wasReadOnly) { (Get-Item $NGHosts).IsReadOnly = $true }
    if (-not $ok) { throw 'Could not update the hosts file (another program may be locking it).' }
    try { Clear-DnsClientCache -ErrorAction SilentlyContinue } catch { }
    return $true
}

function Get-NGAppExes($Cfg) {
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($a in @($Cfg.Blocking.Apps | Where-Object { $_ })) {
        $p = [string]$a
        if (Test-NGProtectedPath $p) { continue }
        if (Test-Path -LiteralPath $p -PathType Leaf) {
            if ($p -like '*.exe' -and -not $out.Contains($p)) { $out.Add($p) }
        } elseif (Test-Path -LiteralPath $p -PathType Container) {
            Get-ChildItem -LiteralPath $p -Recurse -Filter *.exe -File -ErrorAction SilentlyContinue |
                Select-Object -First 300 | ForEach-Object { if (-not $out.Contains($_.FullName)) { $out.Add($_.FullName) } }
        }
    }
    return , $out.ToArray()
}

# Creates outbound block rules for $Exes (or removes them all when empty). Returns $true if anything changed.
function Set-NGFirewallBlock([string[]]$Exes) {
    $sigFile = Join-Path $NGDir 'firewall.sig'
    $existing = @(Get-NetFirewallRule -Group $NGFwGroup -ErrorAction SilentlyContinue)
    if (-not $Exes -or $Exes.Count -eq 0) {
        Remove-Item $sigFile -Force -ErrorAction SilentlyContinue
        if ($existing.Count -gt 0) { $existing | Remove-NetFirewallRule; return $true }
        return $false
    }
    $sig = (($Exes | Sort-Object) -join '|')
    $old = if (Test-Path $sigFile) { (Get-Content $sigFile -Raw).Trim() } else { '' }
    if ($sig -eq $old -and $existing.Count -gt 0) { return $false }
    if ($existing.Count -gt 0) { $existing | Remove-NetFirewallRule }
    foreach ($e in $Exes) {
        New-NetFirewallRule -DisplayName "$NGAppName block: $(Split-Path $e -Leaf)" -Group $NGFwGroup `
            -Direction Outbound -Action Block -Program $e -Profile Any `
            -Description 'Added by NightGuard. Removed automatically when the block ends or on uninstall.' | Out-Null
    }
    if (Test-Path $NGDir) { Set-Content -Path $sigFile -Value $sig }
    return $true
}

function Clear-NGBlocks {
    $a = $false; $b = $false
    try { $a = Set-NGHostsBlock @() } catch { }
    try { $b = Set-NGFirewallBlock @() } catch { }
    return ($a -or $b)
}

# Applies the blocking state that should be in force right now. Returns 'on', 'off' or $null (no change).
function Update-NGBlocking($Cfg) {
    if ($Cfg -and -not (Test-NGExpired $Cfg) -and (Test-NGBlockActive $Cfg)) {
        $h = Set-NGHostsBlock (Get-NGHostsDomains $Cfg)
        $f = Set-NGFirewallBlock (Get-NGAppExes $Cfg)
        if ($h -or $f) { return 'on' }
    } else {
        if (Clear-NGBlocks) { return 'off' }
    }
    return $null
}

# ============================================================================
# Screen time usage (written every minute by tracker.ps1)
# ============================================================================
$NGUsageFile  = Join-Path $NGDir 'usage.json'
$NGNotifyFile = Join-Path $NGDir 'notify.json'
# Never counted as games and never closed, whatever the settings say
$NGNeverKill = @('explorer', 'dwm', 'csrss', 'winlogon', 'wininit', 'lsass', 'services', 'svchost', 'smss', 'logonui',
                 'taskmgr', 'powershell', 'pwsh', 'cmd', 'conhost', 'sihost', 'fontdrvhost', 'ctfmon', 'runtimebroker',
                 'searchhost', 'startmenuexperiencehost', 'shellexperiencehost', 'textinputhost', 'audiodg', 'msmpeng', 'securityhealthsystray')

function Get-NGUsageDay($St, [datetime]$Now = (Get-Date)) { return $Now.AddHours(-[int]$St.ResetHour).Date }

function Get-NGLimits($St, [datetime]$Day) {
    $weekend = ($Day.DayOfWeek -eq 'Saturday' -or $Day.DayOfWeek -eq 'Sunday')
    return [pscustomobject]@{
        Game   = $(if ($weekend) { [int]$St.GameMinutesWeekend } else { [int]$St.GameMinutesWeekday })
        Screen = $(if ($weekend) { [int]$St.ScreenMinutesWeekend } else { [int]$St.ScreenMinutesWeekday })
    }
}

function Format-NGMinutes([double]$M) {
    $m = [math]::Max(0, [math]::Round($M))
    return ('{0}h {1:00}m' -f [math]::Floor($m / 60), ($m % 60))
}

function Get-NGUsageText($Cfg) {
    $st = $Cfg.ScreenTime
    if (-not $st -or -not $st.Enabled) { return 'Screen time limits are off.' }
    $day = Get-NGUsageDay $st
    $lim = Get-NGLimits $st $day
    $game = 0.0; $screen = 0.0
    if (Test-Path $NGUsageFile) {
        try {
            $u = Get-Content $NGUsageFile -Raw | ConvertFrom-Json
            if ($u.Day -eq $day.ToString('yyyy-MM-dd')) { $game = [double]$u.GameMinutes; $screen = [double]$u.ScreenMinutes }
        } catch { }
    }
    $line = {
        param($Name, $Used, $Limit)
        if ($Limit -le 0) { '{0,-7} used {1}   (no limit)' -f $Name, (Format-NGMinutes $Used) }
        else { '{0,-7} used {1} of {2}   left {3}' -f $Name, (Format-NGMinutes $Used), (Format-NGMinutes $Limit), (Format-NGMinutes ($Limit - $Used)) }
    }
    return ("Today ({0}, resets at {1}:00)`r`n  {2}`r`n  {3}" -f $day.ToString('ddd d MMM'), $st.ResetHour, (& $line 'Games' $game $lim.Game), (& $line 'Screen' $screen $lim.Screen))
}

# ============================================================================
# Trusted-person code. 8 random digits. Only a salted PBKDF2 hash is stored,
# in a file only admins can read. No recovery if the code is lost.
# ============================================================================
$NGTrustedFile = Join-Path $NGDir 'trusted.json'
$NGStateFile   = Join-Path $NGDir 'state.json'

function Protect-NGFile([string]$Path) {
    & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
}

# The trusted person picks their own 8-digit code. Returns why a code is too weak/wrong, or $null if it's fine.
function Get-NGCodeProblem([string]$Code) {
    if ($Code -notmatch '^\d{8}$') { return 'The code must be exactly 8 digits.' }
    if ($Code -match '^(\d)\1{7}$') { return 'All the same digit is too easy to guess.' }
    $d = $Code.ToCharArray() | ForEach-Object { [int][string]$_ }
    $up = $true; $down = $true
    for ($i = 1; $i -lt 8; $i++) {
        if ($d[$i] -ne ($d[$i - 1] + 1) % 10) { $up = $false }
        if ($d[$i] -ne ($d[$i - 1] + 9) % 10) { $down = $false }
    }
    if ($up -or $down) { return 'Counting up or down (like 12345678) is too easy to guess.' }
    if ($Code -match '^(\d\d)\1{3}$' -or $Code -match '^(\d{4})\1$') { return 'A repeating pattern (like 12121212) is too easy to guess.' }
    if ($Code -match '^(\d)\1(\d)\2(\d)\3(\d)\4$') { return 'A doubled pattern (like 11223344) is too easy to guess.' }
    if (($Code.ToCharArray() | Select-Object -Unique).Count -le 2) { return 'Use at least 3 different digits.' }
    if ($Code -match '^(19|20)\d\d(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])$' -or $Code -match '^(0[1-9]|[12]\d|3[01])(0[1-9]|1[0-2])(19|20)\d\d$' -or $Code -match '^(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])(19|20)\d\d$') {
        return 'That looks like a date (a birthday is easy to guess). Pick something else.'
    }
    return $null
}


function Get-NGCodeHash([string]$Code, [byte[]]$Salt, [int]$Iterations, [string]$Alg) {
    $norm = ($Code -replace '\D', '')
    if ($Alg -eq 'SHA256') {
        $kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes($norm, $Salt, $Iterations, [Security.Cryptography.HashAlgorithmName]::SHA256)
    } else {
        $kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes($norm, $Salt, $Iterations)
    }
    try { return [Convert]::ToBase64String($kdf.GetBytes(32)) } finally { $kdf.Dispose() }
}

function Get-NGTrusted {
    if (Test-Path $NGTrustedFile) {
        try { $t = Get-Content $NGTrustedFile -Raw | ConvertFrom-Json; if ($t.Enabled) { return $t } } catch { }
    }
    return $null
}

function Get-NGState {
    if (Test-Path $NGStateFile) { try { return (Get-Content $NGStateFile -Raw | ConvertFrom-Json) } catch { } }
    return [pscustomobject]@{ FailedAttempts = 0; LockedUntil = $null }
}

function Save-NGState($State) {
    Set-Content -Path $NGStateFile -Value ($State | ConvertTo-Json) -Encoding UTF8
    Protect-NGFile $NGStateFile
}

# Turns on trusted-person mode with $Code. Requires admin.
function Set-NGTrusted([string]$Code, [string]$Contact) {
    $salt = New-Object byte[] 16
    $rng = New-Object Security.Cryptography.RNGCryptoServiceProvider
    $rng.GetBytes($salt); $rng.Dispose()
    $iter = 300000
    $alg = 'SHA256'
    try { $hash = Get-NGCodeHash $Code $salt $iter $alg }
    catch { $alg = 'SHA1'; $hash = Get-NGCodeHash $Code $salt $iter $alg }   # very old .NET
    $t = [ordered]@{
        Enabled = $true; Contact = $Contact; Alg = $alg; Iterations = $iter
        Salt = [Convert]::ToBase64String($salt); Hash = $hash; CreatedAt = (Get-Date).ToString('s')
    }
    Set-Content -Path $NGTrustedFile -Value ($t | ConvertTo-Json) -Encoding UTF8
    Protect-NGFile $NGTrustedFile
    Save-NGState ([pscustomobject]@{ FailedAttempts = 0; LockedUntil = $null })
}

function Remove-NGTrusted {
    Remove-Item $NGTrustedFile -Force -ErrorAction SilentlyContinue
    Remove-Item $NGStateFile -Force -ErrorAction SilentlyContinue
}

# Throws unless trusted mode is off or $Code is right. Wrong guesses lock you out for longer each time.
function Assert-NGCode([string]$Code) {
    $t = Get-NGTrusted
    if (-not $t) { return }
    $state = Get-NGState
    if ($state.LockedUntil) {
        $until = if ($state.LockedUntil -is [datetime]) { $state.LockedUntil } else { [datetime]::Parse([string]$state.LockedUntil, $NGInv) }
        if ((Get-Date) -lt $until) { throw ("Too many wrong codes. Try again after {0:HH\:mm} ({0:ddd})." -f $until) }
    }
    if ([string]::IsNullOrWhiteSpace($Code)) { throw "This needs the trusted person's code." }
    $hash = Get-NGCodeHash $Code ([Convert]::FromBase64String($t.Salt)) ([int]$t.Iterations) ([string]$t.Alg)
    if ($hash -ceq $t.Hash) {
        Save-NGState ([pscustomobject]@{ FailedAttempts = 0; LockedUntil = $null })
        Write-NGLog 'trusted-person code accepted'
        return
    }
    $fails = [int]$state.FailedAttempts + 1
    $lock = $null
    if ($fails -ge 5) {
        # 15 min after 5 wrong codes, doubling each time after that, up to 24 hours
        $mins = [math]::Min(1440, 15 * [math]::Pow(2, $fails - 5))
        $lock = (Get-Date).AddMinutes($mins).ToString('s')
    }
    Save-NGState ([pscustomobject]@{ FailedAttempts = $fails; LockedUntil = $lock })
    Write-NGLog "wrong trusted-person code (attempt $fails)"
    throw 'That code is wrong.'
}

# ============================================================================
# Self-lock: NightGuard treats itself like a commitment, not a setting.
#  - Making the rules STRICTER is always allowed (outside night mode).
#  - Making them LOOSER, or uninstalling, needs an unlock request and a
#    7-day cool-off. After that the unlock stays open for 3 days, and it is
#    used up by the first loosening save or uninstall.
#  - With an end date, uninstall is not possible before it (NightGuard removes
#    itself after the last night), and the end date can't be moved earlier.
#  - The trusted person's code skips all of this.
#  - A Windows admin can still remove NightGuard by hand (see the README).
# ============================================================================
$NGUnlockFile    = Join-Path $NGDir 'unlock.json'
$NGCoolOffDays   = 7
$NGUnlockOpenDays = 3

function Get-NGUnlock {
    $none = [pscustomobject]@{ State = 'None'; RequestedAt = $null; OpensAt = $null; ClosesAt = $null }
    if (-not (Test-Path $NGUnlockFile)) { return $none }
    try {
        $u = Get-Content $NGUnlockFile -Raw | ConvertFrom-Json
        $req = if ($u.RequestedAt -is [datetime]) { $u.RequestedAt } else { [datetime]::ParseExact([string]$u.RequestedAt, 's', $NGInv) }
    } catch { return $none }
    $now = Get-Date
    if ($req -gt $now.AddDays(1)) { return $none }   # requested "in the future": clock was changed, ignore it
    $opens = $req.AddDays($NGCoolOffDays); $closes = $opens.AddDays($NGUnlockOpenDays)
    $state = if ($now -lt $opens) { 'Waiting' } elseif ($now -lt $closes) { 'Open' } else { 'None' }
    return [pscustomobject]@{ State = $state; RequestedAt = $req; OpensAt = $opens; ClosesAt = $closes }
}

function Request-NGUnlock {
    if ((Get-NGUnlock).State -ne 'None') { return }
    Set-Content -Path $NGUnlockFile -Value (@{ RequestedAt = (Get-Date).ToString('s') } | ConvertTo-Json) -Encoding UTF8
    Protect-NGFile $NGUnlockFile
    Write-NGLog "unlock requested; cool-off ends $((Get-Date).AddDays($NGCoolOffDays).ToString('yyyy-MM-dd HH:mm'))"
}

function Clear-NGUnlock([string]$Why = 'cancelled') {
    if (Test-Path $NGUnlockFile) {
        Remove-Item $NGUnlockFile -Force -ErrorAction SilentlyContinue
        Write-NGLog "unlock request $Why"
    }
}

function Format-NGUnlockText {
    $u = Get-NGUnlock
    switch ($u.State) {
        'Waiting' { return ('Unlock requested. Cool-off ends {0:dddd d MMM, HH:mm}.' -f $u.OpensAt) }
        'Open'    { return ('Unlocked until {0:dddd d MMM, HH:mm}. Looser settings or uninstall are allowed once.' -f $u.ClosesAt) }
        default   { return 'Locked in. Stricter changes are fine anytime; looser ones need an unlock request (7-day cool-off).' }
    }
}

# How strong each bedtime / screen time action is (higher = stricter)
$NGActionRank = @{ Shutdown = 3; Hibernate = 3; SignOut = 2; Sleep = 2; Lock = 1 }
$NGSTActionRank = @{ Shutdown = 3; Lock = 2; Warn = 1 }

# Lists every way $New is looser than $Old. Empty = same or stricter.
function Get-NGLoosening($Old, $New) {
    $out = @()
    if (-not $Old -or -not $New) { return $out }
    foreach ($d in $NGDays) {
        $o = $Old.Schedule.$d; $n = $New.Schedule.$d
        if (-not $o.Enabled) { continue }
        if (-not $n.Enabled) { $out += "$d night is no longer locked"; continue }
        $span = {
            param($s)
            $b = [timespan]::Parse([string]$s.Bedtime).TotalMinutes; $u = [timespan]::Parse([string]$s.Unlock).TotalMinutes
            $start = if ($b -lt 720) { $b + 1440 } else { $b }
            @($start, ($u + 1440))
        }
        $oSpan = & $span $o; $nSpan = & $span $n
        if ($nSpan[0] -gt $oSpan[0]) { $out += "$d night: later bedtime" }
        if ($nSpan[1] -lt $oSpan[1]) { $out += "$d night: earlier unlock" }
    }
    if ($NGActionRank[[string]$New.Action] -lt $NGActionRank[[string]$Old.Action]) { $out += "bedtime action: $($Old.Action) to $($New.Action)" }
    if ([int]$New.CountdownSeconds -gt [int]$Old.CountdownSeconds) { $out += 'longer countdown' }
    if ([int]$New.RecheckMinutes -gt [int]$Old.RecheckMinutes) { $out += 'repeats less often at night' }
    if ([int]$New.NightModeMinutes -lt [int]$Old.NightModeMinutes) { $out += 'shorter night mode' }
    $oe = Get-NGEndDate $Old; $ne = Get-NGEndDate $New
    if ($ne -and (-not $oe -or $ne -lt $oe)) { $out += $(if ($oe) { 'earlier end date' } else { 'new end date' }) }

    $ob = $Old.Blocking; $nb = $New.Blocking
    if ($ob.Enabled) {
        if (-not $nb.Enabled) { $out += 'blocking turned off' }
        else {
            if ($ob.DuringBedtime -and -not $nb.DuringBedtime) { $out += 'no blocking during bedtime' }
            $gone = @($ob.Sites | Where-Object { @($nb.Sites) -notcontains $_ })
            if ($gone.Count) { $out += "sites unblocked: $($gone -join ', ')" }
            $gone = @($ob.Apps | Where-Object { @($nb.Apps) -notcontains $_ })
            if ($gone.Count) { $out += "apps unblocked: $($gone -join ', ')" }
            $key = { param($w) '{0}|{1}|{2}' -f (@($w.Days) -join ','), $w.From, $w.To }
            $nk = @($nb.Windows | ForEach-Object { & $key $_ })
            if (@($ob.Windows | Where-Object { $nk -notcontains (& $key $_) }).Count) { $out += 'blocking time slots changed or removed' }
        }
    }

    $os = $Old.ScreenTime; $ns = $New.ScreenTime
    if ($os.Enabled) {
        if (-not $ns.Enabled) { $out += 'screen time limits turned off' }
        else {
            $names = [ordered]@{ GameMinutesWeekday = 'game time on weekdays'; GameMinutesWeekend = 'game time at weekends'
                                 ScreenMinutesWeekday = 'screen time on weekdays'; ScreenMinutesWeekend = 'screen time at weekends' }
            foreach ($f in $names.Keys) {
                $o = [int]$os.$f; $n = [int]$ns.$f
                if ($o -gt 0 -and ($n -eq 0 -or $n -gt $o)) { $out += "more $($names[$f])" }
            }
            if ($NGSTActionRank[[string]$ns.LimitAction] -lt $NGSTActionRank[[string]$os.LimitAction]) { $out += "screen time action: $($os.LimitAction) to $($ns.LimitAction)" }
            if ([int]$ns.ResetHour -ne [int]$os.ResetHour) { $out += 'different day reset hour' }
            if ([int]$ns.ShutdownGraceMinutes -gt [int]$os.ShutdownGraceMinutes) { $out += 'longer shutdown notice' }
            if ($os.CloseLaunchers -and -not $ns.CloseLaunchers) { $out += 'launchers no longer closed' }
            foreach ($f in 'GameFolders', 'GameProcesses', 'LauncherProcesses') {
                $nl = @($ns.$f | ForEach-Object { "$_".ToLowerInvariant() })
                if (@($os.$f | Where-Object { $nl -notcontains "$_".ToLowerInvariant() }).Count) { $out += "removed from $f" }
            }
            $ol = @($os.IgnoreProcesses | ForEach-Object { "$_".ToLowerInvariant() })
            if (@($ns.IgnoreProcesses | Where-Object { $ol -notcontains "$_".ToLowerInvariant() }).Count) { $out += 'more programs ignored by screen time' }
        }
    }
    return $out
}

# Why uninstalling isn't allowed right now (ignoring the trusted-person code), or $null if it is.
function Get-NGUninstallBlock($Cfg) {
    if (-not $Cfg) { return $null }   # damaged or missing settings: never trap anyone
    $end = Get-NGEndDate $Cfg
    if ($end) { return ("NightGuard is locked in until its last night, {0:dddd d MMMM yyyy}. After that it removes itself." -f $end) }
    switch ((Get-NGUnlock).State) {
        'Open'    { return $null }
        'Waiting' { return ('You asked to uninstall. The 7-day cool-off ends {0:dddd d MMM, HH:mm}. Still want out then? Come back.' -f (Get-NGUnlock).OpensAt) }
        default   { return 'NightGuard is locked in. To uninstall, request an unlock in the NightGuard app. After a 7-day cool-off you can uninstall.' }
    }
}

# ============================================================================
# Removal. Only removes what NightGuard created.
# ============================================================================
# ============================================================================
# Morning check-in, streaks and journal
#  - enforce.ps1 notes each night NightGuard had to step in (stepped-in.txt).
#  - A "clean night" is a guarded night where it didn't have to: you were off
#    before bedtime and stayed off. The streak counts clean nights in a row.
#  - Each user's journal lives in journal\<their SID>\, readable only by that
#    user, SYSTEM and administrators.
# ============================================================================
$NGJournalDir    = Join-Path $NGDir 'journal'
$NGSteppedInFile = Join-Path $NGDir 'stepped-in.txt'
$NGSinceFile     = Join-Path $NGDir 'since.txt'

function Add-NGSteppedIn([datetime]$Evening) {
    try {
        $key = $Evening.ToString('yyyy-MM-dd')
        $have = @(); if (Test-Path $NGSteppedInFile) { $have = @(Get-Content $NGSteppedInFile) }
        if ($have -contains $key) { return }
        @($have + $key) | Select-Object -Last 400 | Set-Content -Path $NGSteppedInFile -Encoding ASCII
    } catch { }
}

function Get-NGSince {
    try { return [datetime]::ParseExact((Get-Content $NGSinceFile -TotalCount 1).Trim(), 'yyyy-MM-dd', $NGInv) } catch { return (Get-Date).Date }
}

# How did last night go? Returns $null while a locked night is still running, else
# an object with Kind = Clean | SteppedIn | Free | New, and the current streak.
function Get-NGNightReport($Cfg, [datetime]$Now = (Get-Date)) {
    if (Get-NGCurrentNight $Cfg) { return $null }
    $since = Get-NGSince
    $stepped = @(); if (Test-Path $NGSteppedInFile) { $stepped = @(Get-Content $NGSteppedInFile) }
    # Walk every night since install (oldest first): current run and best run of clean nights
    $streak = 0; $best = 0
    $first = $since; if (($Now.Date - $first).TotalDays -gt 800) { $first = $Now.Date.AddDays(-800) }
    for ($eve = $first; $eve -lt $Now.Date; $eve = $eve.AddDays(1)) {
        $n = Get-NGNight $Cfg $eve -IgnoreEndDate
        if (-not $n -or $n.End -gt $Now) { continue }       # free night (doesn't break the streak)
        if ($stepped -contains $eve.ToString('yyyy-MM-dd')) { $streak = 0 } else { $streak++ }
        if ($streak -gt $best) { $best = $streak }
    }
    $y = $Now.Date.AddDays(-1)
    $last = Get-NGNight $Cfg $y -IgnoreEndDate
    $kind = if ($y -lt $since) { 'New' } elseif (-not $last) { 'Free' } elseif ($stepped -contains $y.ToString('yyyy-MM-dd')) { 'SteppedIn' } else { 'Clean' }
    return [pscustomobject]@{ Kind = $kind; Streak = $streak; Best = $best }
}

function Get-NGJournalFolder([switch]$Create) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $dir = Join-Path $NGJournalDir $sid
    if ($Create -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    return $dir
}

function Get-NGJournal {
    $f = Join-Path (Get-NGJournalFolder) 'journal.json'
    if (-not (Test-Path $f)) { return @() }
    try { $raw = Get-Content $f -Raw -Encoding UTF8 | ConvertFrom-Json; return @($raw | ForEach-Object { $_ }) } catch { return @() }
}

function Add-NGJournalEntry($Entry) {
    $f = Join-Path (Get-NGJournalFolder -Create) 'journal.json'
    $list = New-Object System.Collections.ArrayList
    foreach ($e in (Get-NGJournal)) { [void]$list.Add($e) }
    [void]$list.Add($Entry)
    ConvertTo-Json -InputObject @($list | Select-Object -Last 1000) -Depth 4 | Set-Content -Path $f -Encoding UTF8
}

$NGMoodNames = @{ 1 = 'Rough'; 2 = 'Meh'; 3 = 'OK'; 4 = 'Good'; 5 = 'Great' }

# ============================================================================
# Popups that get through to someone in a game
#  - Always-on-top window, pulled to the foreground (with the usual ALT-key trick,
#    since Windows blocks background apps from stealing focus), re-asserted every
#    second so a game can't bury it.
#  - Taking focus also knocks most exclusive-fullscreen games back to the desktop.
#  - A sound, plus a Windows notification as a backup.
# ============================================================================
function Initialize-NGWin32 {
    if ('NGNative.NGWin32' -as [type]) { return }
    Add-Type -Namespace 'NGNative' -Name 'NGWin32' -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
'@
}

# Puts a form on top of everything and gives it focus
function Set-NGFront($Form, [switch]$NoActivate) {
    try {
        Initialize-NGWin32
        $h = $Form.Handle
        if ($NoActivate) {
            [void][NGNative.NGWin32]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0010)   # HWND_TOPMOST, no move/size/activate
            return
        }
        [void][NGNative.NGWin32]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)      # HWND_TOPMOST, show
        if ([NGNative.NGWin32]::GetForegroundWindow() -ne $h) {
            [NGNative.NGWin32]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [NGNative.NGWin32]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)   # tap ALT
            [void][NGNative.NGWin32]::ShowWindow($h, 9)   # restore
            [void][NGNative.NGWin32]::BringWindowToTop($h)
            [void][NGNative.NGWin32]::SetForegroundWindow($h)
        }
    } catch { }
}

# Keeps a form on top while it's open: grabs focus for the first few seconds,
# then keeps it above other windows every second without stealing typing.
function Enable-NGStayOnTop($Form, [int]$GrabSeconds = 5) {
    $Form.TopMost = $true
    $t = New-Object System.Windows.Forms.Timer
    $t.Interval = 1000
    $t.Tag = @{ Form = $Form; Ticks = 0; Grab = $GrabSeconds }
    $t.Add_Tick({
        $st = $this.Tag; $st.Ticks++
        if ($st.Ticks -le $st.Grab) { Set-NGFront $st.Form } else { Set-NGFront $st.Form -NoActivate }
    })
    $Form.Tag = $t
    $Form.Add_Shown({ Set-NGFront $this; $this.Tag.Start() })
    $Form.Add_FormClosed({ try { $this.Tag.Stop(); $this.Tag.Dispose() } catch { } })
}

# Backup: a Windows notification (shows even when a window can't get through; Focus Assist may hold it)
function Show-NGToast([string]$Title, [string]$Text) {
    try {
        $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
        $esc = { param($s) [Security.SecurityElement]::Escape($s) }
        $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
        $xml.LoadXml("<toast scenario=`"reminder`"><visual><binding template=`"ToastGeneric`"><text>$(& $esc $Title)</text><text>$(& $esc $Text)</text></binding></visual><audio src=`"ms-winsoundevent:Notification.Looping.Alarm`"/><actions><action content=`"OK`" arguments=`"ok`"/></actions></toast>")
        $appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show([Windows.UI.Notifications.ToastNotification]::new($xml))
    } catch { }
}

# ============================================================================
# Badges: streak milestones of clean nights, from night one to a full year
# ============================================================================
$NGBadges = @(
    @{ Nights = 1;   Name = 'First Light';          Line = 'One clean night. Every legend starts somewhere.' }
    @{ Nights = 3;   Name = 'Hat Trick';            Line = 'Three nights. Your pillow is starting to recognise you.' }
    @{ Nights = 5;   Name = 'High Five';            Line = 'Five nights. Give yourself one. Then go to bed.' }
    @{ Nights = 7;   Name = 'Week Warrior';         Line = 'A full week! Science says the habit is forming. We say: nice.' }
    @{ Nights = 10;  Name = 'Double Digits';        Line = 'Ten nights. "One more game" is losing its grip.' }
    @{ Nights = 14;  Name = 'Fortnight Knight';     Line = 'Two weeks. Your eye bags have filed a complaint about being ignored.' }
    @{ Nights = 21;  Name = 'Habit Hacker';         Line = '21 nights. They say that is how long a habit takes. You did it.' }
    @{ Nights = 30;  Name = 'Moonwalker';           Line = 'A whole month of real sleep. Who even are you?' }
    @{ Nights = 45;  Name = 'Sandman''s Apprentice'; Line = '45 nights. The Sandman wants to hire you.' }
    @{ Nights = 60;  Name = 'Dream Team';           Line = 'Two months. Your future self sends a thank-you card.' }
    @{ Nights = 75;  Name = 'Pillow Pro';           Line = '75 nights. You and your pillow are officially a couple.' }
    @{ Nights = 90;  Name = 'Quarter Master';       Line = 'Three months. A whole season of not seeing 3 AM.' }
    @{ Nights = 120; Name = 'Night Owl, Retired';   Line = '120 nights. The owl has hung up its wings.' }
    @{ Nights = 150; Name = 'Sleep Scholar';        Line = '150 nights. You could teach a class on this.' }
    @{ Nights = 180; Name = 'Half-Year Hero';       Line = 'Six months! Half a year of choosing sleep.' }
    @{ Nights = 225; Name = 'Snooze Sage';          Line = '225 nights. Monks come to you for advice now.' }
    @{ Nights = 270; Name = 'Nine-Month Ninja';     Line = '270 nights. Silent, rested, unstoppable.' }
    @{ Nights = 300; Name = 'Three Hundred Club';   Line = '300 nights. This is just who you are now.' }
    @{ Nights = 330; Name = 'Almost There';         Line = '330 nights. The finish line can see you.' }
    @{ Nights = 365; Name = 'Sleep Legend';         Line = 'ONE YEAR. A full trip around the sun, asleep on time. Absolute legend.' }
)
function Get-NGBadges([int]$Best) { @($NGBadges | Where-Object { $_.Nights -le $Best }) }
function Get-NGNextBadge([int]$Best) { @($NGBadges | Where-Object { $_.Nights -gt $Best })[0] }

function Remove-NightGuard {
    try { [void](Clear-NGBlocks) } catch { }
    Get-ScheduledTask -TaskPath $NGTaskPath -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
    try {
        $svc = New-Object -ComObject 'Schedule.Service'
        $svc.Connect()
        $svc.GetFolder('\').DeleteFolder($NGAppName, 0)
    } catch { }
    Remove-Item -Path $NGShortcut, $NGShortcut2, $NGShortcutDesk -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $NGDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Removes an install of the tool under its old name (Bedtime Lock). Returns its
# settings (or $null) so the installer can carry them over.
function Remove-NGLegacy {
    $old = $null
    $f = Join-Path $NGLegacyDir 'config.json'
    if (Test-Path $f) { try { $old = Read-NGConfigFile $f } catch { } }
    Get-ScheduledTask -TaskPath "\$NGLegacyName\" -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
    try {
        $svc = New-Object -ComObject 'Schedule.Service'
        $svc.Connect()
        $svc.GetFolder('\').DeleteFolder($NGLegacyName, 0)
    } catch { }
    Remove-Item -Path (Join-Path $NGStartMenu 'Bedtime Lock.lnk'), (Join-Path $NGStartMenu 'Bedtime Lock (text menu).lnk') -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $NGLegacyDir -Recurse -Force -ErrorAction SilentlyContinue
    return $old
}
