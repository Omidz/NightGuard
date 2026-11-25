<#
  NightGuard - installer. Usually run from the menu (NightGuard.cmd).
    .\install.ps1                          # install with current settings, or defaults
    .\install.ps1 -ConfigPath my.json      # install with settings from a file
#>
param(
    [string]$ConfigPath = '',
    [string]$Code = '',          # trusted-person code, when that mode is on
    [switch]$NoPause,
    [switch]$ThrowOnError        # used by the windowed app
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

# --- Re-launch elevated if needed -------------------------------------------
$me = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $me.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Asking for administrator rights...'
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($ConfigPath) { $argList += @('-ConfigPath', "`"$((Resolve-Path $ConfigPath).Path)`"") }
    if ($Code) { $argList += @('-Code', $Code) }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList
    exit
}

try {
    $current = Get-NGConfig
    $legacyCfg = $null
    $legacyFile = Join-Path $NGLegacyDir 'config.json'
    if (Test-Path $legacyFile) { try { $legacyCfg = Read-NGConfigFile $legacyFile } catch { } }

    # No loosening the rules at night (also applies to an old Bedtime Lock install)
    $blocked = Get-NGBlockedReason $(if ($current) { $current } else { $legacyCfg })
    if ($blocked) { throw $blocked }

    # Trusted-person mode: every change needs the code
    Assert-NGCode $Code   # does nothing when trusted-person mode is off
    $codeOk = [bool](Get-NGTrusted)   # a correct code also skips the cool-off

    # Upgrading from the old name (Bedtime Lock)? Remove it; its settings carry over.
    if (Test-Path $NGLegacyDir) {
        [void](Remove-NGLegacy)
        Write-Host 'Removed the old Bedtime Lock install. Your settings carry over.'
    }

    if ($ConfigPath)   { $cfg = Read-NGConfigFile $ConfigPath }
    elseif ($current)  { $cfg = $current }
    elseif ($legacyCfg) { $cfg = $legacyCfg }
    else               { $cfg = Copy-NGConfig (New-NGDefaultConfig) }

    # Screen Limits add-on installed separately? Its limits move into NightGuard.
    $oldSL = Get-NGOldScreenLimits
    if ($oldSL -and -not $ConfigPath -and -not $cfg.ScreenTime.Enabled) { $cfg.ScreenTime = $oldSL; $cfg = Copy-NGConfig $cfg }

    $errs = Test-NGConfig $cfg
    if ($errs.Count -gt 0) { throw ("Settings have problems:`n  - " + ($errs -join "`n  - ")) }
    if (Test-NGExpired $cfg) { throw "The end date $($cfg.EndDate) has already passed." }

    # Self-lock: stricter is always fine; looser needs an open unlock (7-day cool-off) or the code
    $usedUnlock = $false
    if ($current -and -not $codeOk) {
        $looser = @(Get-NGLoosening $current $cfg)
        if ($looser.Count -gt 0) {
            if ((Get-NGEndDate $current) -and (@($looser) -contains 'earlier end date')) {
                throw "The end date can't be moved earlier. NightGuard is locked in until $($current.EndDate)."
            }
            if ((Get-NGUnlock).State -ne 'Open') {
                throw ("These changes make NightGuard looser:`n  - " + ($looser -join "`n  - ") +
                       "`n`n$(Format-NGUnlockText)`nStricter changes can be saved right away.")
            }
            $usedUnlock = $true
        }
    }

    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

    # --- Copy scripts to a folder only admins can change ---------------------
    New-Item -ItemType Directory -Force -Path $NGDir | Out-Null
    $src = (Resolve-Path $PSScriptRoot).Path.TrimEnd('\')
    if ($src -ne $NGDir.TrimEnd('\')) {
        foreach ($f in $NGScripts) {
            Copy-Item -Path (Join-Path $src $f) -Destination $NGDir -Force
        }
    }
    Set-Content -Path (Join-Path $NGDir 'config.json') -Value (ConvertTo-NGJson $cfg) -Encoding UTF8
    # Copies keep the 'downloaded' mark; remove it so SmartScreen won't block the installed menu
    Get-ChildItem -Path $NGDir -File | Unblock-File -ErrorAction SilentlyContinue

    # SYSTEM + Administrators: full control. Users: read/execute only.
    & icacls.exe $NGDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed with exit code $LASTEXITCODE" }

    # Morning check-in journal: each user gets a private subfolder (only they, SYSTEM and admins can read it)
    if (-not (Test-Path $NGSinceFile)) { Set-Content -Path $NGSinceFile -Value (Get-Date).ToString('yyyy-MM-dd') -Encoding ASCII }
    New-Item -ItemType Directory -Force -Path $NGJournalDir | Out-Null
    & icacls.exe $NGJournalDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-3-0:(OI)(CI)(IO)F' '*S-1-5-32-545:(RD,AD,X,RA)' | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-NGLog "journal folder permissions could not be set (icacls exit $LASTEXITCODE)" }

    # Remove any older install of the tasks
    Get-ScheduledTask -TaskPath $NGTaskPath -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false

    # --- Build weekly triggers from the schedule ------------------------------
    # Use a reference week (2024-01-01 is a Monday) to work out the weekday and
    # time of each bedtime and each warning, including ones that cross midnight.
    $nightTriggers = @()
    $warnTriggers  = @()
    foreach ($i in 0..6) {
        $n = Get-NGNight $cfg (([datetime]'2024-01-01').AddDays($i)) -IgnoreEndDate
        if (-not $n) { continue }

        $t = New-ScheduledTaskTrigger -Weekly -DaysOfWeek $n.Start.DayOfWeek -At $n.Start.ToString('HH:mm')
        $rep = New-ScheduledTaskTrigger -Once -At $n.Start.ToString('HH:mm') `
            -RepetitionInterval (New-TimeSpan -Minutes ([int]$cfg.RecheckMinutes)) -RepetitionDuration ($n.End - $n.Start)
        $t.Repetition = $rep.Repetition
        $nightTriggers += $t

        foreach ($w in @($cfg.Warnings | Where-Object { $_ })) {
            $at = $n.Start.AddMinutes(-[int]$w.MinutesBefore)
            $warnTriggers += New-ScheduledTaskTrigger -Weekly -DaysOfWeek $at.DayOfWeek -At $at.ToString('HH:mm')
        }
    }
    $boot  = New-ScheduledTaskTrigger -AtStartup
    $logon = New-ScheduledTaskTrigger -AtLogOn

    $system = New-ScheduledTaskPrincipal -UserId 'NT AUTHORITY\SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $usersGroup = (New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-545').Translate([Security.Principal.NTAccount]).Value
    $users = New-ScheduledTaskPrincipal -GroupId $usersGroup -RunLevel Limited

    function New-NGAction([string]$Script) {
        New-ScheduledTaskAction -Execute $ps -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$NGDir\$Script`""
    }
    $limit = New-TimeSpan -Seconds ([int]$cfg.CountdownSeconds + 300)

    # --- Task 1: Enforce (SYSTEM): shutdown / sleep / hibernate, and end-date cleanup
    # Only wake the PC for a shutdown; waking it just to sleep again would be silly.
    $sysSettingsArgs = @{
        AllowStartIfOnBatteries = $true; DontStopIfGoingOnBatteries = $true; StartWhenAvailable = $true
        MultipleInstances = 'IgnoreNew'; ExecutionTimeLimit = $limit
    }
    if ($cfg.Action -eq 'Shutdown') { $sysSettingsArgs.WakeToRun = $true }
    Register-ScheduledTask -TaskPath $NGTaskPath -TaskName 'Enforce bedtime' `
        -Action (New-NGAction 'enforce.ps1') -Trigger (@($nightTriggers) + $boot + $logon) -Principal $system `
        -Settings (New-ScheduledTaskSettingsSet @sysSettingsArgs) `
        -Description 'Enforces the bedtime action during locked nights. Installed by NightGuard.' | Out-Null

    # --- Task 2: Session (each signed-in user): countdown, lock / sign out ----
    Register-ScheduledTask -TaskPath $NGTaskPath -TaskName 'Bedtime countdown' `
        -Action (New-NGAction 'session.ps1') -Trigger (@($nightTriggers) + $logon) -Principal $users `
        -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
                    -MultipleInstances IgnoreNew -ExecutionTimeLimit $limit) `
        -Description 'Shows the bedtime countdown and locks or signs out. Installed by NightGuard.' | Out-Null

    # --- Task 3: Warnings (each signed-in user) -------------------------------
    if ($warnTriggers.Count -gt 0) {
        Register-ScheduledTask -TaskPath $NGTaskPath -TaskName 'Bedtime warnings' `
            -Action (New-NGAction 'warn.ps1') -Trigger $warnTriggers -Principal $users `
            -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                        -MultipleInstances Parallel -ExecutionTimeLimit (New-TimeSpan -Minutes 10)) `
            -Description 'Shows bedtime warning popups. Installed by NightGuard.' | Out-Null
    }

    # --- Task 4: Site/app blocking (SYSTEM), every 5 minutes and at startup ---
    # Always registered: when blocking is off it simply makes sure nothing is blocked.
    $every5 = New-ScheduledTaskTrigger -Daily -At '00:00'
    $every5.Repetition = (New-ScheduledTaskTrigger -Once -At '00:00' -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 1)).Repetition
    Register-ScheduledTask -TaskPath $NGTaskPath -TaskName 'Site and app blocking' `
        -Action (New-NGAction 'blocker.ps1') -Trigger @($every5, $boot) -Principal $system `
        -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
                    -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 5)) `
        -Description 'Turns NightGuard site and app blocking on and off on schedule. Installed by NightGuard.' | Out-Null

    # --- Tasks 5 and 6: Screen time tracker (SYSTEM, every minute) and popups (user session)
    if ($cfg.ScreenTime.Enabled) {
        $everyMin = New-ScheduledTaskTrigger -Daily -At '00:00'
        $everyMin.Repetition = (New-ScheduledTaskTrigger -Once -At '00:00' -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 1)).Repetition
        Register-ScheduledTask -TaskPath $NGTaskPath -TaskName 'Screen time tracker' `
            -Action (New-NGAction 'tracker.ps1') -Trigger @($everyMin, $boot) -Principal $system `
            -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
                        -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 2)) `
            -Description 'Counts daily game and screen time and enforces the limits. Installed by NightGuard.' | Out-Null
        Register-ScheduledTask -TaskPath $NGTaskPath -TaskName 'Screen time notify' `
            -Action (New-NGAction 'notify.ps1') -Principal $users `
            -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                        -MultipleInstances Parallel -ExecutionTimeLimit (New-TimeSpan -Minutes 5)) `
            -Description 'Shows screen time popups. Installed by NightGuard.' | Out-Null
    }

    # --- Task 7: Morning check-in (each signed-in user), at sign-in and unlock
    if ($cfg.MorningCheckIn) {
        $checkTriggers = @($logon)
        try {
            $cls = Get-CimClass -ClassName MSFT_TaskSessionStateChangeTrigger -Namespace 'Root/Microsoft/Windows/TaskScheduler'
            $checkTriggers += New-CimInstance -CimClass $cls -ClientOnly -Property @{ StateChange = 8 }   # 8 = session unlock
        } catch { }
        Register-ScheduledTask -TaskPath $NGTaskPath -TaskName 'Morning check-in' `
            -Action (New-NGAction 'checkin.ps1') -Trigger $checkTriggers -Principal $users `
            -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                        -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 15)) `
            -Description 'Shows a morning pat on the back and an optional mood journal. Installed by NightGuard.' | Out-Null
    }

    # Old Screen Limits add-on: keep today's usage, then remove it
    if (Test-Path $NGOldSLDir) {
        $oldUsage = Join-Path $NGOldSLDir 'state.json'
        if ((Test-Path $oldUsage) -and -not (Test-Path $NGUsageFile)) { Copy-Item $oldUsage $NGUsageFile -Force }
        Remove-NGOldScreenLimits
        Write-Host 'Moved the separate Screen Limits add-on into NightGuard.'
        Write-NGLog 'old Screen Limits add-on migrated and removed'
    }

    # Apply blocking right away
    try {
        $r = Update-NGBlocking $cfg
        if ($r) { Write-NGLog "site/app blocking turned $r" }
    } catch {
        [void](Clear-NGBlocks)
        Write-Host "Site/app blocking could not be applied: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    # --- Start Menu shortcuts ---------------------------------------------------
    try {
        $wsh = New-Object -ComObject WScript.Shell
        # All shortcuts point at the installed copy, never at the downloaded folder
        Remove-Item -Path $NGShortcut, $NGShortcut2, $NGShortcutDesk -Force -ErrorAction SilentlyContinue
        $places = @()
        if ($cfg.ShortcutStartMenu -ne $false) { $places += $NGShortcut }
        if ($cfg.ShortcutDesktop) { $places += $NGShortcutDesk }
        foreach ($path in $places) {
            $lnk = $wsh.CreateShortcut($path)
            $lnk.TargetPath = $ps
            $lnk.Arguments  = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$NGDir\gui.ps1`""
            $lnk.WorkingDirectory = $NGDir
            $lnk.Description = 'NightGuard'
            $lnk.Save()
        }
        if ($cfg.ShortcutStartMenu -ne $false) {
            $lnk = $wsh.CreateShortcut($NGShortcut2)
            $lnk.TargetPath = $ps
            $lnk.Arguments  = "-NoProfile -ExecutionPolicy Bypass -File `"$NGDir\menu.ps1`""
            $lnk.WorkingDirectory = $NGDir
            $lnk.Description = 'NightGuard (text menu)'
            $lnk.Save()
        }
    } catch { }

    Write-Host ''
    if ($usedUnlock) { Clear-NGUnlock 'used for looser settings' }
    Write-Host 'Saved. NightGuard is installed and your settings are active.' -ForegroundColor Green
    Write-Host '  Open it from: Start Menu > NightGuard'
    Write-NGLog 'settings saved'
}
catch {
    if ($ThrowOnError) { throw }
    Write-Host ''
    Write-Host "Not saved: $($_.Exception.Message)" -ForegroundColor Red
}
if (-not $NoPause) { Write-Host ''; Read-Host 'Press Enter to close' | Out-Null }
