# NightGuard - runs in the signed-in user's session during a locked night.
# Shows the countdown, then locks or signs out (for Lock / SignOut actions).
# For Sleep / Hibernate it only shows the countdown; SYSTEM does the rest.
# Shutdown uses Windows' own shutdown notice, so nothing to do here.
# -Preview shows a 10-second countdown and does nothing afterwards.
param([switch]$Preview)

. (Join-Path $PSScriptRoot 'common.ps1')
$cfgPath = Join-Path $PSScriptRoot 'config.json'
try { $cfg = if (Test-Path $cfgPath) { Read-NGConfigFile $cfgPath } else { New-NGDefaultConfig } } catch { exit 0 }

if (-not $Preview) {
    if (Test-NGSafeMode) { exit 0 }
    if (Get-NGUnsafeReason $cfg) { exit 0 }
    if (Test-NGExpired $cfg) { exit 0 }
    if (-not (Get-NGCurrentNight $cfg)) { exit 0 }
    if ($cfg.Action -eq 'Shutdown') { exit 0 }
}

$secs  = if ($Preview) { 10 } else { [int]$cfg.CountdownSeconds }
$words = $NGActionWords[[string]$cfg.Action]

if ($secs -gt 0) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $form = New-Object System.Windows.Forms.Form -Property @{
        Text = 'NightGuard'; TopMost = $true; StartPosition = 'CenterScreen'
        Width = 520; Height = 200; FormBorderStyle = 'FixedDialog'; MaximizeBox = $false; MinimizeBox = $false
        BackColor = [System.Drawing.Color]::FromArgb(30, 30, 40); ShowInTaskbar = $true
    }
    $label = New-Object System.Windows.Forms.Label -Property @{
        Dock = 'Fill'; TextAlign = 'MiddleCenter'; ForeColor = [System.Drawing.Color]::White
        Font = New-Object System.Drawing.Font('Segoe UI', 14)
    }
    $form.Controls.Add($label)
    $script:left = $secs
    $prefix = if ($Preview) { 'PREVIEW (nothing will happen)' + "`n" } else { "It's bedtime.`n" }
    $label.Text = "${prefix}This PC will $words in $script:left seconds."
    $timer = New-Object System.Windows.Forms.Timer -Property @{ Interval = 1000 }
    $timer.Add_Tick({
        $script:left--
        if ($script:left -le 0) { $timer.Stop(); $form.Close() }
        else { $label.Text = "${prefix}This PC will $words in $script:left seconds." }
    })
    $form.Add_Shown({ $form.Activate(); [System.Media.SystemSounds]::Exclamation.Play(); $timer.Start() })
    [void]$form.ShowDialog()   # closing it early just acts sooner
    $timer.Dispose(); $form.Dispose()
}

if ($Preview) { exit 0 }

switch ([string]$cfg.Action) {
    'Lock'    { & rundll32.exe 'user32.dll,LockWorkStation' }
    'SignOut' { & shutdown.exe /l /f }
    default   { }
}
