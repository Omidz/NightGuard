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

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if ($req.Lock) { [System.Media.SystemSounds]::Exclamation.Play() } else { [System.Media.SystemSounds]::Asterisk.Play() }
Show-NGToast 'NightGuard' ([string]$req.Message)

# Always-on-top window that pulls itself in front of games; closes itself after 10 s (lock) or 60 s
$form = New-Object System.Windows.Forms.Form -Property @{
    Text = 'NightGuard'; StartPosition = 'CenterScreen'; Width = 500; Height = 220; FormBorderStyle = 'FixedDialog'
    MaximizeBox = $false; MinimizeBox = $false; BackColor = [System.Drawing.Color]::White }
$lbl = New-Object System.Windows.Forms.Label -Property @{ Location = (New-Object System.Drawing.Point(16, 16)); Size = (New-Object System.Drawing.Size(452, 110))
    Font = (New-Object System.Drawing.Font('Segoe UI', 11)); Text = [string]$req.Message }
$ok = New-Object System.Windows.Forms.Button -Property @{ Text = 'OK'; Location = (New-Object System.Drawing.Point(368, 130)); Size = (New-Object System.Drawing.Size(100, 34)); DialogResult = 'OK' }
$form.Controls.AddRange(@($lbl, $ok)); $form.AcceptButton = $ok
$close = New-Object System.Windows.Forms.Timer -Property @{ Interval = $(if ($req.Lock) { 10000 } else { 60000 }) }
$close.Add_Tick({ $close.Stop(); $form.Close() })
$form.Add_Shown({ $close.Start() })
Enable-NGStayOnTop $form 3
[void]$form.ShowDialog()
$close.Dispose(); $form.Dispose()

if ($req.Lock) { & "$env:SystemRoot\System32\rundll32.exe" 'user32.dll,LockWorkStation' }
