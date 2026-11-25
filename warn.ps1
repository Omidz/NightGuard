# NightGuard - warning popup. Runs in the signed-in user's session.
# Picks the warning whose "minutes before bedtime" matches now.
# -Preview -Index N shows warning N (1-based) right away.
param([switch]$Preview, [int]$Index = 1)

Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'common.ps1')

$cfgPath = Join-Path $PSScriptRoot 'config.json'
$cfg = if (Test-Path $cfgPath) { Read-NGConfigFile $cfgPath } else { New-NGDefaultConfig }
$warnings = @($cfg.Warnings | Where-Object { $_ })
if ($warnings.Count -eq 0) { exit 0 }

$now = Get-Date
if ($Preview) {
    $night = Get-NGNextNight $cfg $now
    if (-not $night) { $night = Get-NGNight $cfg $now -IgnoreEndDate }
    if (-not $night) { $night = [pscustomobject]@{ Start = $now.Date.AddDays(1); End = $now.Date.AddDays(1).AddHours(6) } }
    $w = $warnings[[math]::Min([math]::Max($Index, 1), $warnings.Count) - 1]
    $mins = [int]$w.MinutesBefore
} else {
    if (Test-NGExpired $cfg) { exit 0 }
    $night = Get-NGNextNight $cfg $now
    if (-not $night) { exit 0 }
    $mins = [int][math]::Ceiling(($night.Start - $now).TotalMinutes)
    # The warning scheduled for this moment (allow a little drift)
    $w = $warnings | Sort-Object { [math]::Abs([int]$_.MinutesBefore - $mins) } | Select-Object -First 1
    if ([math]::Abs([int]$w.MinutesBefore - $mins) -gt 3) { exit 0 }
}

$text = Format-NGMessage ([string]$w.Message) $cfg $night $mins
if ($Preview) { $text = "PREVIEW`n`n$text" }
$icon = if ([int]$w.MinutesBefore -le 5) { [System.Windows.Forms.MessageBoxIcon]::Error } else { [System.Windows.Forms.MessageBoxIcon]::Warning }

if ($w.Sound) { [System.Media.SystemSounds]::Exclamation.Play() }

# Invisible always-on-top owner window so the popup appears above other windows
$owner = New-Object System.Windows.Forms.Form -Property @{ TopMost = $true; ShowInTaskbar = $false; WindowState = 'Minimized' }
$owner.Show()
$owner.Hide()
[System.Windows.Forms.MessageBox]::Show($owner, $text, 'NightGuard', [System.Windows.Forms.MessageBoxButtons]::OK, $icon) | Out-Null
$owner.Dispose()
