# NightGuard - warning popup. Runs in the signed-in user's session.
# Picks the warning whose "minutes before bedtime" matches now.
# -Preview -Index N shows warning N (1-based) right away.
param([switch]$Preview, [int]$Index = 1)

Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'common.ps1')

$cfgPath = Join-Path $PSScriptRoot 'config.json'
try { $cfg = if (Test-Path $cfgPath) { Read-NGConfigFile $cfgPath } else { New-NGDefaultConfig } } catch { exit 0 }
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
# The last warning before bedtime also reminds you why you started
$final = ([int]$w.MinutesBefore -eq (@($warnings | ForEach-Object { [int]$_.MinutesBefore }) | Measure-Object -Minimum).Minimum)
if ($final -and $cfg.Why) { $text += "`n`nRemember why you started:`n" + [char]0x201C + [string]$cfg.Why + [char]0x201D }
if ($Preview) { $text = "PREVIEW`n`n$text" }
$urgent = [int]$w.MinutesBefore -le 5

Add-Type -AssemblyName System.Drawing
if ($w.Sound) { [System.Media.SystemSounds]::Exclamation.Play() }
Show-NGToast 'NightGuard' (($text -replace "`n+", ' ').Substring(0, [math]::Min(200, ($text -replace "`n+", ' ').Length)))

# Always-on-top window that pulls itself in front of games (see Enable-NGStayOnTop)
$form = New-Object System.Windows.Forms.Form -Property @{
    Text = 'NightGuard'; StartPosition = 'CenterScreen'; Width = 560; Height = 340; FormBorderStyle = 'FixedDialog'
    MaximizeBox = $false; MinimizeBox = $false; ShowInTaskbar = $true; BackColor = [System.Drawing.Color]::White
}
$hdr = New-Object System.Windows.Forms.Panel -Property @{ Dock = 'Top'; Height = 54
    BackColor = $(if ($urgent) { [System.Drawing.Color]::FromArgb(200, 30, 30) } else { [System.Drawing.Color]::FromArgb(40, 50, 120) }) }
$hdr.Controls.Add((New-Object System.Windows.Forms.Label -Property @{ Dock = 'Fill'; TextAlign = 'MiddleLeft'; ForeColor = [System.Drawing.Color]::White
    Padding = (New-Object System.Windows.Forms.Padding(14, 0, 0, 0)); Font = (New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold))
    Text = $(if ($urgent) { 'Final call!' } else { 'Bedtime is coming' }) }))
$lbl = New-Object System.Windows.Forms.Label -Property @{ Location = (New-Object System.Drawing.Point(16, 66)); Size = (New-Object System.Drawing.Size(512, 170))
    Font = (New-Object System.Drawing.Font('Segoe UI', 11)); Text = $text }
$ok = New-Object System.Windows.Forms.Button -Property @{ Text = 'Got it'; Location = (New-Object System.Drawing.Point(428, 246)); Size = (New-Object System.Drawing.Size(100, 36)); DialogResult = 'OK' }
$form.Controls.AddRange(@($lbl, $ok, $hdr))
$form.AcceptButton = $ok
# Grow to fit long messages
$need = [System.Windows.Forms.TextRenderer]::MeasureText($text, $lbl.Font, (New-Object System.Drawing.Size(512, 2000)), [System.Windows.Forms.TextFormatFlags]::WordBreak).Height
if ($need -gt 170) { $extra = [math]::Min(300, $need - 170); $lbl.Height += $extra; $ok.Top += $extra; $form.Height += $extra }
Enable-NGStayOnTop $form
[void]$form.ShowDialog()
$form.Dispose()
