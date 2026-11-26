# NightGuard - runs in the signed-in user's session during a locked night.
# Shows the countdown, then locks or signs out (for Lock / SignOut actions).
# For Sleep / Hibernate it only shows the countdown; SYSTEM does the rest.
# For Shutdown it shows the countdown too (Windows' own notice is easy to miss in a game).
# -Preview shows a 10-second countdown and does nothing afterwards
# (-ConfigPath: preview with these settings instead of the installed ones).
# Resume safety: if the PC sleeps or hibernates while the countdown is showing,
# the countdown closes when the PC wakes up and nothing happens; the next
# scheduled run decides afresh whether it's still bedtime.
param([switch]$Preview, [string]$ConfigPath = '')

. (Join-Path $PSScriptRoot 'common.ps1')
$cfgPath = if ($Preview -and $ConfigPath) { $ConfigPath } else { Join-Path $PSScriptRoot 'config.json' }
try { $cfg = if (Test-Path $cfgPath) { Read-NGConfigFile $cfgPath } else { New-NGDefaultConfig } } catch { exit 0 }

if (-not $Preview) {
    if (Test-NGSafeMode) { exit 0 }
    if (Get-NGUnsafeReason $cfg) { exit 0 }
    if (Test-NGExpired $cfg) { exit 0 }
    if (-not (Get-NGCurrentNight $cfg)) { exit 0 }
}

$secs  = if ($Preview) { 10 } else { [int]$cfg.CountdownSeconds }
$words = $NGActionWords[[string]$cfg.Action]

if ($secs -gt 0) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $form = New-Object System.Windows.Forms.Form -Property @{
        Text = 'NightGuard'; TopMost = $true; StartPosition = 'CenterScreen'
        Width = 560; Height = $(if ($cfg.Why) { 280 } else { 200 }); FormBorderStyle = 'FixedDialog'; MaximizeBox = $false; MinimizeBox = $false
        BackColor = [System.Drawing.Color]::FromArgb(30, 30, 40); ShowInTaskbar = $true
    }
    $label = New-Object System.Windows.Forms.Label -Property @{
        Dock = 'Fill'; TextAlign = 'MiddleCenter'; ForeColor = [System.Drawing.Color]::White
        Font = New-Object System.Drawing.Font('Segoe UI', $(if ($cfg.Why) { 12 } else { 14 }))
    }
    $form.Controls.Add($label)
    $script:left = $secs
    $prefix = if ($Preview) { 'PREVIEW (nothing will happen)' + "`n" } else { "It's bedtime.`n" }
    $suffix = if ($cfg.Why) { "`n`nYou said: " + [char]0x201C + [string]$cfg.Why + [char]0x201D } else { '' }
    $label.Text = "${prefix}This PC will $words in $script:left seconds.$suffix"
    $script:stale = $false
    $script:deadline = [datetime]::UtcNow.AddSeconds($secs)
    $script:lastTick = [datetime]::UtcNow
    $timer = New-Object System.Windows.Forms.Timer -Property @{ Interval = 500 }
    $timer.Add_Tick({
        $t = [datetime]::UtcNow
        # A gap of more than 10 s between ticks: the PC slept / hibernated (or the clock
        # jumped). This countdown is from before that, so it closes without acting.
        if (($t - $script:lastTick).TotalSeconds -gt 10 -or $t -lt $script:lastTick.AddSeconds(-1)) { $script:stale = $true; $timer.Stop(); $form.Close(); return }
        $script:lastTick = $t
        $script:left = [int][math]::Ceiling(($script:deadline - $t).TotalSeconds)
        if ($script:left -le 0) { $timer.Stop(); $form.Close() }
        else { $label.Text = "${prefix}This PC will $words in $script:left seconds.$suffix" }
    })
    # Pull the countdown in front of games for its whole length (see Enable-NGStayOnTop)
    Enable-NGStayOnTop $form $secs
    $form.Add_Shown({ [System.Media.SystemSounds]::Exclamation.Play(); $timer.Start() })
    if (-not $Preview) { Show-NGToast 'NightGuard: bedtime' "This PC will $words in $secs seconds. Save now." }
    [void]$form.ShowDialog()   # closing it early just acts sooner
    $timer.Dispose(); $form.Dispose()
}

if ($Preview) { exit 0 }
# Stale (slept in the middle) or bedtime already over: do nothing
if ($script:stale -or -not (Get-NGCurrentNight $cfg)) { exit 0 }

switch ([string]$cfg.Action) {
    'Lock'    { & rundll32.exe 'user32.dll,LockWorkStation' }
    'SignOut' { & shutdown.exe /l /f }
    default   { }
}
