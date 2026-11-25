# NightGuard - screen time popups. Runs in the signed-in user's session
# (task "Screen time notify", started by tracker.ps1). Shows the latest message
# from notify.json as an always-on-top popup and, if asked, locks the PC.

. (Join-Path $PSScriptRoot 'common.ps1')

try { $req = Get-Content -Raw -Path $NGNotifyFile | ConvertFrom-Json } catch { exit 0 }

# Ignore stale requests (for example after waking from sleep)
try {
    $sent = if ($req.Time -is [datetime]) { $req.Time } else { [datetime]::Parse([string]$req.Time, $null, [Globalization.DateTimeStyles]::RoundtripKind) }
    if (((Get-Date) - $sent).TotalMinutes -gt 2) { exit 0 }
} catch { }

$shell = New-Object -ComObject WScript.Shell
$flags = 0x40 + 0x1000   # information icon + stay on top

if ($req.Lock) {
    [System.Media.SystemSounds]::Exclamation.Play()
    $null = $shell.Popup([string]$req.Message, 10, 'NightGuard', $flags)
    & "$env:SystemRoot\System32\rundll32.exe" 'user32.dll,LockWorkStation'
} else {
    [System.Media.SystemSounds]::Asterisk.Play()
    $null = $shell.Popup([string]$req.Message, 60, 'NightGuard', $flags)
}
