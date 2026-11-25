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
#   Blocking          { Enabled, DuringBedtime, Sites[], Apps[], Windows[ {Days[], From, To} ] }
#                     Sites are blocked in the hosts file, Apps (exe files or folders) with Windows Firewall.
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
$NGDays      = @('Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun')
$NGActions   = @('Shutdown', 'Sleep', 'Hibernate', 'Lock', 'SignOut')
$NGActionWords = @{ Shutdown = 'shut down'; Sleep = 'go to sleep'; Hibernate = 'hibernate'; Lock = 'lock'; SignOut = 'sign you out' }
$NGTimeRegex = '^([01]?\d|2[0-3]):[0-5]\d$'
$NGInv       = [Globalization.CultureInfo]::InvariantCulture
$NGHosts     = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$NGHostsBegin = "# >>> $NGAppName BEGIN - managed by $NGAppName, removed automatically"
$NGHostsEnd   = "# <<< $NGAppName END"
$NGFwGroup   = $NGAppName
$NGScripts   = @('common.ps1', 'enforce.ps1', 'session.ps1', 'warn.ps1', 'blocker.ps1', 'install.ps1', 'uninstall.ps1', 'menu.ps1', 'gui.ps1', "$NGAppName.cmd", 'TextMenu.cmd')
# Earlier name of this tool; old installs are migrated and removed on install
$NGLegacyName = 'BedtimeLock'
$NGLegacyDir  = Join-Path $env:ProgramData $NGLegacyName
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
        Action           = 'Shutdown'
        CountdownSeconds = 60
        RecheckMinutes   = 5
        NightModeMinutes = 60
        EndDate          = $null
        Blocking         = New-NGDefaultBlocking
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
        Blocking         = [ordered]@{
            Enabled       = [bool]$b.Enabled
            DuringBedtime = [bool]$b.DuringBedtime
            Sites         = (ConvertTo-NGStringArray ($b.Sites | ForEach-Object { "$_".ToLowerInvariant() }))
            Apps          = (ConvertTo-NGStringArray $b.Apps)
            Windows       = $wins
        }
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

function Get-NGConfig {
    $f = Join-Path $NGDir 'config.json'
    if (Test-Path $f) { return Read-NGConfigFile $f }
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

# Writes (or removes, when $Domains is empty) the NightGuard section. Returns $true if the file changed.
function Set-NGHostsBlock([string[]]$Domains) {
    $orig = ''
    if (Test-Path $NGHosts) { $orig = [IO.File]::ReadAllText($NGHosts) }
    $pattern = '(?ms)^# >>> ' + [regex]::Escape($NGAppName) + ' BEGIN.*?^# <<< ' + [regex]::Escape($NGAppName) + ' END[^\n]*\n?'
    $clean = [regex]::Replace($orig, $pattern, '')
    $new = $clean
    if ($Domains -and $Domains.Count -gt 0) {
        if ($new.Length -gt 0 -and -not $new.EndsWith("`n")) { $new += "`r`n" }
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
# Trusted-person code. 8 random digits. Only a salted PBKDF2 hash is stored,
# in a file only admins can read. No recovery if the code is lost.
# ============================================================================
$NGTrustedFile = Join-Path $NGDir 'trusted.json'
$NGStateFile   = Join-Path $NGDir 'state.json'

function Protect-NGFile([string]$Path) {
    & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
}

function New-NGCode {
    $rng = New-Object Security.Cryptography.RNGCryptoServiceProvider
    $digits = ''
    $buf = New-Object byte[] 1
    while ($digits.Length -lt 8) {
        $rng.GetBytes($buf)
        if ($buf[0] -lt 250) { $digits += [string]($buf[0] % 10) }   # no bias
    }
    $rng.Dispose()
    return $digits
}

function Format-NGCode([string]$Code) { return ($Code.Substring(0, 4) + ' ' + $Code.Substring(4, 4)) }

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
# Removal. Only removes what NightGuard created.
# ============================================================================
function Remove-NightGuard {
    try { [void](Clear-NGBlocks) } catch { }
    Get-ScheduledTask -TaskPath $NGTaskPath -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
    try {
        $svc = New-Object -ComObject 'Schedule.Service'
        $svc.Connect()
        $svc.GetFolder('\').DeleteFolder($NGAppName, 0)
    } catch { }
    Remove-Item -Path $NGShortcut, $NGShortcut2 -Force -ErrorAction SilentlyContinue
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
