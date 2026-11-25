# NightGuard - morning check-in. Runs in the signed-in user's session at sign-in
# and unlock (task "Morning check-in"), shows at most once a day:
# a pat on the back for last night, the streak, and an optional mood + note
# that go into this user's private journal.

. (Join-Path $PSScriptRoot 'common.ps1')

try {
    if (Test-NGSafeMode) { exit 0 }
    $cfg = Get-NGConfig
    if (-not $cfg -or $cfg.MorningCheckIn -eq $false -or (Test-NGExpired $cfg) -or (Get-NGUnsafeReason $cfg)) { exit 0 }
    $report = Get-NGNightReport $cfg
    if (-not $report -or $report.Kind -eq 'New') { exit 0 }   # inside a locked night, or no night yet since install

    # Once a day
    $dir = Get-NGJournalFolder -Create
    $mark = Join-Path $dir 'last-checkin.txt'
    $today = (Get-Date).ToString('yyyy-MM-dd')
    if ((Test-Path $mark) -and ((Get-Content $mark -TotalCount 1) -eq $today)) { exit 0 }
    Set-Content -Path $mark -Value $today -Encoding ASCII
} catch { exit 0 }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

function E([int]$Code) { [char]::ConvertFromUtf32($Code) }
$pick = { param($List) $List[(Get-Random -Maximum $List.Count)] }

switch ($report.Kind) {
    'Clean' {
        $title = & $pick @('Good morning, legend!', 'Look at you!', 'Another night, another win!', 'Well rested? We think so.')
        $body  = & $pick @(
            "You were off before bedtime last night. Sleep: 1, One More Game: 0.",
            "Last night you logged off like a responsible adult. Your pillow is proud of you.",
            "No bouncer needed last night. You did that all by yourself.",
            "You beat the 'just one more' monster last night. Respect.")
    }
    'SteppedIn' {
        $title = & $pick @('Morning! Rough night?', 'Hey, it happens.', 'New day, clean slate.')
        $body  = & $pick @(
            "NightGuard had to tuck you in last night. No judgment. Well, a little. Tonight's a fresh start.",
            "The bouncer had to step in last night. That's what it's here for. Try to beat it to bedtime tonight.",
            "Last night went into overtime. Streak resets, but you're still here, and that counts.")
    }
    default {
        $title = 'Good morning!'
        $body  = "Last night was a free night. Hope it was a good one. Back to the routine tonight!"
    }
}
$streakTxt = switch ($report.Streak) {
    0       { 'Streak: 0. Tonight is night one.' }
    1       { "$(E 0x1F525) 1 clean night. Every streak starts somewhere." }
    default { "$(E 0x1F525) $($report.Streak) clean nights in a row!" + $(if ($report.Streak -ge 7) { ' That is a habit now.' } else { '' }) }
}

# New badge earned last night?
$badge = $null
# (only the first time you reach it: the streak is at a milestone and is your best ever)
if ($report.Kind -eq 'Clean' -and $report.Streak -eq $report.Best) { $badge = $NGBadges | Where-Object { $_.Nights -eq $report.Streak } | Select-Object -First 1 }
if ($badge) { $title = "New badge: $($badge.Name)!"; $body = "$($badge.Line)`n$body" }
$next = Get-NGNextBadge ([math]::Max($report.Best, $report.Streak))
if ($next) { $streakTxt += "  Next badge: $($next.Name) at $($next.Nights)." }
$whyTxt = if ($cfg.Why) { 'Remember why you started: ' + [char]0x201C + [string]$cfg.Why + [char]0x201D } else { '' }
$shift = if ($whyTxt) { 50 } else { 0 }

$FontRow = New-Object System.Drawing.Font('Segoe UI', 10)
$form = New-Object System.Windows.Forms.Form
$form.Text = 'NightGuard - morning check-in'; $form.Size = New-Object System.Drawing.Size(560, (470 + $shift))
$form.StartPosition = 'CenterScreen'; $form.FormBorderStyle = 'FixedDialog'; $form.MaximizeBox = $false; $form.MinimizeBox = $false
$form.TopMost = $true; $form.BackColor = [System.Drawing.Color]::White; $form.Font = $FontRow

function Add-Ctl([string]$Type, [hashtable]$Props) {
    $c = New-Object "System.Windows.Forms.$Type"
    foreach ($k in $Props.Keys) { $c.$k = $Props[$k] }
    [void]$form.Controls.Add($c); return $c
}
function P([int]$X, [int]$Y) { New-Object System.Drawing.Point($X, $Y) }
function S([int]$W, [int]$H) { New-Object System.Drawing.Size($W, $H) }

$hdr = Add-Ctl 'Panel' @{ Location = (P 0 0); Size = (S 560 64); BackColor = [System.Drawing.Color]::FromArgb(40, 50, 120) }
$t = New-Object System.Windows.Forms.Label
$t.Text = $title; $t.Location = (P 18 16); $t.Size = (S 520 34); $t.ForeColor = [System.Drawing.Color]::White
$t.Font = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)
[void]$hdr.Controls.Add($t)
Add-Ctl 'Label' @{ Text = $body; Location = (P 18 72); Size = (S 520 54) } | Out-Null
if ($whyTxt) { Add-Ctl 'Label' @{ Text = $whyTxt; Location = (P 18 162); Size = (S 520 46); ForeColor = [System.Drawing.Color]::FromArgb(40, 50, 120)
    Font = (New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Italic)) } | Out-Null }
Add-Ctl 'Label' @{ Text = $streakTxt; Location = (P 18 128); Size = (S 520 30); Font = (New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold))
    ForeColor = [System.Drawing.Color]::FromArgb(190, 90, 0) } | Out-Null

Add-Ctl 'Label' @{ Text = 'How are you feeling this morning? (optional)'; Location = (P 18 (170 + $shift)); Size = (S 520 22); Font = (New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)) } | Out-Null
$faces = @{ 1 = 0x1F62B; 2 = 0x1F615; 3 = 0x1F610; 4 = 0x1F642; 5 = 0x1F601 }
$script:mood = 0
$moodBtns = @()
for ($m = 1; $m -le 5; $m++) {
    $b = Add-Ctl 'Button' @{ Text = "$(E $faces[$m])`n$($NGMoodNames[$m])"; Location = (P (18 + ($m - 1) * 102) (196 + $shift)); Size = (S 94 62); Tag = $m
        Font = (New-Object System.Drawing.Font('Segoe UI Emoji', 10)); FlatStyle = 'Flat'; BackColor = [System.Drawing.Color]::White }
    $moodBtns += $b
}
foreach ($b in $moodBtns) {
    $b.Add_Click({ param($s, $e)
        $script:mood = [int]$s.Tag
        foreach ($x in $moodBtns) { $x.BackColor = $(if ([int]$x.Tag -eq $script:mood) { [System.Drawing.Color]::FromArgb(200, 215, 255) } else { [System.Drawing.Color]::White }) }
    })
}
Add-Ctl 'Label' @{ Text = 'A short note for future you (optional, stays on this PC):'; Location = (P 18 (270 + $shift)); Size = (S 520 22) } | Out-Null
$note = Add-Ctl 'TextBox' @{ Location = (P 18 (294 + $shift)); Size = (S 510 56); Multiline = $true; MaxLength = 280 }
$save = Add-Ctl 'Button' @{ Text = 'Save to my journal'; Location = (P 268 (370 + $shift)); Size = (S 160 36); Font = (New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)); DialogResult = 'OK' }
$skip = Add-Ctl 'Button' @{ Text = 'Skip'; Location = (P 438 (370 + $shift)); Size = (S 90 36); DialogResult = 'Cancel' }
$form.CancelButton = $skip

# Close by itself after 10 minutes if nobody's there
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 600000
$timer.Add_Tick({ $timer.Stop(); $form.DialogResult = 'Cancel'; $form.Close() })
$timer.Start()

$r = $form.ShowDialog()
$timer.Stop()
if ($r -eq 'OK' -and ($script:mood -gt 0 -or $note.Text.Trim())) {
    try {
        Add-NGJournalEntry ([pscustomobject]@{
            Date = (Get-Date).ToString('yyyy-MM-dd'); Time = (Get-Date).ToString('HH:mm')
            Mood = $script:mood; Note = $note.Text.Trim(); LastNight = $report.Kind; Streak = $report.Streak
            Badge = $(if ($badge) { $badge.Name } else { '' })
        })
    } catch { }
}
$form.Dispose()
