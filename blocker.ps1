# NightGuard - site and app blocker. Runs as SYSTEM every few minutes and at startup.
# Turns the hosts-file section and the "NightGuard" firewall rules on or off to match
# the blocking schedule. If anything is wrong (no settings, expired, Safe Mode, error)
# it removes the blocks, so a problem never leaves sites blocked.

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
}
catch {
    Write-NGLog "blocking error: $($_.Exception.Message). Removing blocks to be safe."
    try { [void](Clear-NGBlocks) } catch { }
}
