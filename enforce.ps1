# NightGuard - enforcer. Runs as SYSTEM from Task Scheduler.
# Inside a locked night: shuts down / sleeps / hibernates after the countdown.
# Lock and SignOut are done by session.ps1 in the user's session; this script
# steps in as a backup if that countdown was closed or killed.
# After the end date: removes NightGuard completely.
#
# Resume safety: every countdown is timed by the wall clock (Wait-NGSeconds). If
# the PC slept or hibernated in the middle, this run is stale and stops without
# acting; the next scheduled run (or the resume trigger) decides afresh. Before
# acting it always checks again that it is still bedtime.

. (Join-Path $PSScriptRoot 'common.ps1')
$cfg = Get-NGConfig
if (-not $cfg) { exit 0 }

# Tasks normally don't run in Safe Mode; never act there, so Safe Mode always works.
if (Test-NGSafeMode) { exit 0 }

if (Test-NGExpired $cfg) {
    Write-NGLog 'end date reached, removing NightGuard'
    Remove-NightGuard
    exit 0
}

# Invalid settings or a wrong clock: do nothing rather than risk a shutdown loop
$unsafe = Get-NGUnsafeReason $cfg
if ($unsafe) { Write-NGLog "not enforcing: $unsafe"; exit 0 }

$night = Get-NGCurrentNight $cfg
if (-not $night) {
    # Daytime (for example just resumed from last night's hibernate): nothing to do,
    # except cancel a bedtime shutdown that was left pending across a sleep.
    Clear-NGStaleShutdown
    exit 0
}
# Someone was actually using the PC during a locked night (signed in, screen not locked):
# no "clean night" for the morning check-in streak. A PC woken from sleep to the lock screen doesn't count.
if (Test-NGScreenInUse) { Add-NGSteppedIn $night.Evening }

# Waits out the countdown. Stops this run if the PC slept / hibernated meanwhile,
# or if bedtime is over by the time the countdown ends.
function Wait-Countdown([int]$Seconds) {
    if (-not (Wait-NGSeconds $Seconds)) { Write-NGLog 'the PC slept or the clock jumped during the countdown; this run stops'; exit 0 }
    if (-not (Get-NGCurrentNight $cfg)) { Write-NGLog 'bedtime ended during the countdown; nothing to do'; exit 0 }
}

$secs = [int]$cfg.CountdownSeconds
$shutdownExe = Join-Path $env:SystemRoot 'System32\shutdown.exe'
switch ([string]$cfg.Action) {
    'Shutdown' {
        $msg = "NightGuard: it's past $($night.Start.ToString('HH:mm')). This PC is shutting down and stays locked until $($night.End.ToString('HH:mm')). Go to sleep."
        & $shutdownExe /s /f /t $secs /c $msg 2>$null
        Write-NGLog ("shutdown requested (exit code {0})" -f $LASTEXITCODE)   # 1190 = already scheduled
        Set-Content -Path $NGShutdownMark -Value (Get-Date).ToString('s') -ErrorAction SilentlyContinue
        # Any user can cancel a pending shutdown (shutdown /a). If we're still here well
        # after the countdown, it was cancelled: shut down now, with no countdown to cancel.
        Wait-Countdown ($secs + 15)
        Write-NGLog 'the shutdown was cancelled; shutting down now'
        & $shutdownExe /s /f /t 0 /c $msg 2>$null
    }
    'Hibernate' {
        Wait-Countdown $secs
        Write-NGLog 'hibernating'
        & $shutdownExe /h 2>$null
        if ($LASTEXITCODE -ne 0 -and (Get-NGCurrentNight $cfg)) {
            Write-NGLog ("hibernate failed (exit code {0}), shutting down instead" -f $LASTEXITCODE)
            & $shutdownExe /s /f /t 0 2>$null
        }
    }
    'Sleep' {
        Wait-Countdown $secs
        Write-NGLog 'sleeping'
        Add-Type -AssemblyName System.Windows.Forms
        [void][System.Windows.Forms.Application]::SetSuspendState([System.Windows.Forms.PowerState]::Suspend, $true, $false)
    }
    default {
        # Lock / SignOut: session.ps1 does it after its countdown. Give it a little
        # extra time, then do it from here if the screen is still in use.
        Wait-Countdown ($secs + 20)
        if (-not (Test-NGScreenInUse)) { exit 0 }
        try {
            Initialize-NGWts
            $sid = [NGNative.NGWts]::WTSGetActiveConsoleSessionId()
            if ($sid -eq [uint32]::MaxValue) { exit 0 }
            if ([string]$cfg.Action -eq 'SignOut') {
                Write-NGLog 'countdown window was closed; signing out from here'
                [void][NGNative.NGWts]::WTSLogoffSession([IntPtr]::Zero, $sid, $false)
            } else {
                Write-NGLog 'countdown window was closed; locking from here'
                [void][NGNative.NGWts]::WTSDisconnectSession([IntPtr]::Zero, $sid, $false)
            }
        } catch { Write-NGLog "could not lock or sign out: $($_.Exception.Message)" }
    }
}
