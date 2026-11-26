# NightGuard - site and app blocker. Runs as SYSTEM every few minutes and at startup.
# Turns the hosts-file section and the "NightGuard" firewall rules on or off to match
# the blocking schedule. If anything is wrong (no settings, expired, Safe Mode, error)
# it removes the blocks, so a problem never leaves sites blocked.
#
# It is also the backup for bedtime: if it's bedtime by NightGuard's clock but the
# bedtime tasks didn't fire (for example the Windows time zone was changed to dodge
# them), it starts them. Outside bedtime it cancels a leftover bedtime shutdown.

. (Join-Path $PSScriptRoot 'common.ps1')

try {
    $cfg = Get-NGConfig
    $unsafe = if (Test-NGSafeMode) { 'Safe Mode' } else { Get-NGUnsafeReason $cfg }
    if ($unsafe) {
        if (Clear-NGBlocks) { Write-NGLog "blocking removed ($unsafe)" }
        exit 0
    }
    $r = Update-NGBlocking $cfg
    if ($r) { Write-NGLog "site/app blocking turned $r" }

    if (-not (Test-NGExpired $cfg)) {
        if (Get-NGCurrentNight $cfg) {
            # Already running = ignored (the tasks don't start a second copy)
            foreach ($task in 'Enforce bedtime', 'Bedtime countdown') {
                try { Start-ScheduledTask -TaskPath $NGTaskPath -TaskName $task -ErrorAction Stop } catch { }
            }
        } else {
            Clear-NGStaleShutdown
        }
    }
}
catch {
    Write-NGLog "blocking error: $($_.Exception.Message). Removing blocks to be safe."
    try { [void](Clear-NGBlocks) } catch { }
}
