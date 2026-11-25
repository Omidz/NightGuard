# NightGuard - enforcer. Runs as SYSTEM from Task Scheduler.
# Inside a locked night: shuts down / sleeps / hibernates after the countdown.
# (Lock and SignOut are done by session.ps1 in the user's session.)
# After the end date: removes NightGuard completely.

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
if (-not $night) { exit 0 }
# Someone was actually using the PC during a locked night (signed in, screen not locked):
# no "clean night" for the morning check-in streak. A PC woken from sleep to the lock screen doesn't count.
try {
    $user = (Get-CimInstance Win32_ComputerSystem).UserName
    $lockScreen = [bool](Get-Process -Name LogonUI -ErrorAction SilentlyContinue)
    if ($user -and -not $lockScreen) { Add-NGSteppedIn $night.Evening }
} catch { }

$secs = [int]$cfg.CountdownSeconds
switch ([string]$cfg.Action) {
    'Shutdown' {
        $msg = "NightGuard: it's past $($night.Start.ToString('HH:mm')). This PC is shutting down and stays locked until $($night.End.ToString('HH:mm')). Go to sleep."
        & shutdown.exe /s /f /t $secs /c $msg 2>$null
        Write-NGLog ("shutdown requested (exit code {0})" -f $LASTEXITCODE)   # 1190 = already scheduled
    }
    'Hibernate' {
        Start-Sleep -Seconds $secs
        Write-NGLog 'hibernating'
        & shutdown.exe /h 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-NGLog ("hibernate failed (exit code {0}), shutting down instead" -f $LASTEXITCODE)
            & shutdown.exe /s /f /t 0 2>$null
        }
    }
    'Sleep' {
        Start-Sleep -Seconds $secs
        Write-NGLog 'sleeping'
        Add-Type -AssemblyName System.Windows.Forms
        [void][System.Windows.Forms.Application]::SetSuspendState([System.Windows.Forms.PowerState]::Suspend, $true, $false)
    }
    default { }   # Lock / SignOut handled in the user's session
}
