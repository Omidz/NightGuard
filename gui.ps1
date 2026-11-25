# NightGuard - windowed app. Start it with NightGuard.cmd or Start Menu > NightGuard.
# The text menu (TextMenu.cmd / menu.ps1) does the same basics if this window ever fails.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

# Remove the 'downloaded from the internet' mark so SmartScreen stops blocking these files
Get-ChildItem -Path $PSScriptRoot -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

$me = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $me.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$PSCommandPath`"")
    exit
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$FontUI   = New-Object System.Drawing.Font('Segoe UI', 9)
$FontBold = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$FontBig  = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
$FontMono = New-Object System.Drawing.Font('Consolas', 10)
$FontRow  = New-Object System.Drawing.Font('Segoe UI', 10)
$FontRowB = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
$Red      = [System.Drawing.Color]::FromArgb(200, 30, 30)
$Gray     = [System.Drawing.Color]::DimGray

# Times are shown the way the Windows clock shows them: 12-hour (AM/PM) or 24-hour
$Is12h   = [Globalization.CultureInfo]::CurrentCulture.DateTimeFormat.ShortTimePattern -match 't'
$TimeFmt = if ($Is12h) { 'h:mm tt' } else { 'HH:mm' }
$TimeHint = if ($Is12h) { 'Times use the 12-hour clock (AM / PM), same as your Windows clock.' } else { 'Times use the 24-hour clock (00:00 is midnight), same as your Windows clock.' }

# ============================================================================
# Small helpers
# ============================================================================
function P([int]$X, [int]$Y) { New-Object System.Drawing.Point($X, $Y) }
function S([int]$W, [int]$H) { New-Object System.Drawing.Size($W, $H) }

function New-Ctl([string]$Type, $Parent, [hashtable]$Props) {
    $c = New-Object "System.Windows.Forms.$Type"
    foreach ($k in $Props.Keys) { $c.$k = $Props[$k] }
    if ($Parent) { [void]$Parent.Controls.Add($c) }
    return $c
}

function Msg([string]$Text, [string]$Icon = 'Information') {
    [void][System.Windows.Forms.MessageBox]::Show($script:form, $Text, 'NightGuard', 'OK', $Icon)
}

function Confirm-Ask([string]$Text) {
    return ([System.Windows.Forms.MessageBox]::Show($script:form, $Text, 'NightGuard', 'YesNo', 'Question') -eq 'Yes')
}

function Safe([scriptblock]$Block) {
    try { & $Block } catch { Msg $_.Exception.Message 'Error' }
}

# --- Time pickers: click the hour or minute (or AM/PM) and use the arrows, or type
function New-TimePicker($Parent, [int]$X, [int]$Y, [string]$Value = '00:00') {
    $t = New-Ctl 'DateTimePicker' $Parent @{ Location = (P $X $Y); Size = (S 112 28); Format = 'Custom'; CustomFormat = $TimeFmt; ShowUpDown = $true; Font = $FontRow }
    Set-PickerTime $t $Value
    return $t
}
function Set-PickerTime($Picker, [string]$HHmm) {
    if ($HHmm -notmatch $NGTimeRegex) { $HHmm = '00:00' }
    $Picker.Value = [datetime]::Today.Add([timespan]::Parse($HHmm))
}
function Get-PickerTime($Picker) { return $Picker.Value.ToString('HH:mm', $NGInv) }
function Format-LocalTime([string]$HHmm) { return ([datetime]::Today.Add([timespan]::Parse($HHmm))).ToString($TimeFmt) }

function Normalize-Time([string]$T) {
    $T = "$T".Trim()
    if ($T -match $NGTimeRegex) { return ([timespan]::Parse($T)).ToString('hh\:mm') }
    return $T
}

# ============================================================================
# Code dialogs (trusted-person mode)
# ============================================================================
function Show-CodeWarning {
    $d = New-Ctl 'Form' $null @{ Text = 'NightGuard - read this first'; Size = (S 560 430); StartPosition = 'CenterParent'
        FormBorderStyle = 'FixedDialog'; MaximizeBox = $false; MinimizeBox = $false; Font = $FontUI; BackColor = [System.Drawing.Color]::White }
    $hdr = New-Ctl 'Panel' $d @{ Location = (P 0 0); Size = (S 560 70); BackColor = $Red }
    New-Ctl 'Label' $hdr @{ Text = 'WARNING: THERE IS NO WAY TO RECOVER A LOST CODE'; Location = (P 16 22); Size = (S 520 30)
        ForeColor = [System.Drawing.Color]::White; Font = $FontBig } | Out-Null
    New-Ctl 'Label' $d @{ Location = (P 16 84); Size = (S 520 210); Font = (New-Object System.Drawing.Font('Segoe UI', 10)); Text = @'
Trusted-person mode creates an 8-digit code. After you turn it on:

  - Changing ANY setting or uninstalling NightGuard needs the code.
  - The code is shown ONCE. This PC only keeps a scrambled copy.
  - If the code is lost, NOBODY can recover it. Not you, not the
    trusted person, not the makers of this tool.

Give the code to someone you trust (email, print or write it down)
BEFORE you continue. You GOT TO HAVE THE CODE.
'@ } | Out-Null
    $chk = New-Ctl 'CheckBox' $d @{ Text = 'I understand: if the code is lost, it can never be recovered.'; Location = (P 16 300); Size = (S 520 24); Font = $FontBold }
    $ok = New-Ctl 'Button' $d @{ Text = 'Continue'; Location = (P 330 340); Size = (S 100 32); Enabled = $false; DialogResult = 'OK' }
    New-Ctl 'Button' $d @{ Text = 'Cancel'; Location = (P 440 340); Size = (S 90 32); DialogResult = 'Cancel' } | Out-Null
    $chk.Add_CheckedChanged({ $ok.Enabled = $chk.Checked })
    $d.AcceptButton = $ok
    $r = $d.ShowDialog($script:form)
    $d.Dispose()
    return ($r -eq 'OK')
}

function Send-CodeEmail([string]$Code, [string]$To) {
    $subject = 'NightGuard code - please keep this safe'
    $body = "Hi,`r`n`r`nI'm using NightGuard to stop myself staying up late on my PC. You are my trusted person.`r`n`r`n" +
            "Please keep this code safe, and only give it to me if you agree I really need to change the settings or remove it:`r`n`r`n" +
            "    $(Format-NGCode $Code)`r`n`r`n" +
            "IMPORTANT: if this code is lost, there is no way to recover it.`r`n`r`nThanks!"
    $uri = 'mailto:' + [uri]::EscapeDataString($To) + '?subject=' + [uri]::EscapeDataString($subject) + '&body=' + [uri]::EscapeDataString($body)
    try { Start-Process $uri }
    catch { Msg "No email app is set up on this PC. Use Copy and paste the code into an email yourself, or use Print." 'Warning' }
}

function Print-Code([string]$Code, [string]$To) {
    $f = Join-Path $env:TEMP ('nightguard-code-' + [guid]::NewGuid().ToString('N') + '.txt')
    $text = "BEDTIME LOCK - TRUSTED PERSON CODE`r`n`r`n        $(Format-NGCode $Code)`r`n`r`n" +
            "Created: $(Get-Date -Format 'yyyy-MM-dd HH:mm')`r`n" + $(if ($To) { "Trusted person: $To`r`n" } else { '' }) +
            "`r`nThis code is needed to change settings or uninstall NightGuard.`r`nIF THIS CODE IS LOST, IT CAN NEVER BE RECOVERED. Keep it safe.`r`n"
    Set-Content -Path $f -Value $text -Encoding UTF8
    try { Start-Process notepad.exe -ArgumentList @('/p', "`"$f`"") -Wait }
    finally { Remove-Item $f -Force -ErrorAction SilentlyContinue }
}

# Shows a new code; returns $true only once the user typed it back and confirmed.
function Show-NewCode([string]$Code, [string]$To) {
    $d = New-Ctl 'Form' $null @{ Text = 'NightGuard - your trusted-person code'; Size = (S 560 500); StartPosition = 'CenterParent'
        FormBorderStyle = 'FixedDialog'; MaximizeBox = $false; MinimizeBox = $false; Font = $FontUI; BackColor = [System.Drawing.Color]::White }
    $hdr = New-Ctl 'Panel' $d @{ Location = (P 0 0); Size = (S 560 50); BackColor = $Red }
    New-Ctl 'Label' $hdr @{ Text = 'This code is shown ONCE. It can never be recovered.'; Location = (P 16 14); Size = (S 520 26)
        ForeColor = [System.Drawing.Color]::White; Font = $FontBig } | Out-Null
    New-Ctl 'Label' $d @{ Text = (Format-NGCode $Code); Location = (P 16 64); Size = (S 520 70); TextAlign = 'MiddleCenter'
        Font = (New-Object System.Drawing.Font('Consolas', 36, [System.Drawing.FontStyle]::Bold)); BorderStyle = 'FixedSingle' } | Out-Null
    $bEmail = New-Ctl 'Button' $d @{ Text = 'Email it to them'; Location = (P 16 146); Size = (S 160 32) }
    $bCopy  = New-Ctl 'Button' $d @{ Text = 'Copy'; Location = (P 186 146); Size = (S 100 32) }
    $bPrint = New-Ctl 'Button' $d @{ Text = 'Print'; Location = (P 296 146); Size = (S 100 32) }
    New-Ctl 'Label' $d @{ Location = (P 16 190); Size = (S 520 60); Text = "Send or print it now and give it to your trusted person. Don't keep the only copy yourself, or the lock is easy to undo. Don't lose it either: without it, settings and uninstall stay locked." } | Out-Null
    New-Ctl 'Label' $d @{ Text = 'Type the code here to confirm you have it:'; Location = (P 16 256); Size = (S 400 20); Font = $FontBold } | Out-Null
    $txt = New-Ctl 'TextBox' $d @{ Location = (P 16 278); Size = (S 200 26); Font = $FontMono; MaxLength = 12 }
    $chk = New-Ctl 'CheckBox' $d @{ Text = 'My trusted person has the code, and I understand it can never be recovered.'; Location = (P 16 316); Size = (S 520 24) }
    $ok = New-Ctl 'Button' $d @{ Text = 'Turn it on'; Location = (P 310 410); Size = (S 110 32); Enabled = $false; DialogResult = 'OK' }
    New-Ctl 'Button' $d @{ Text = 'Cancel'; Location = (P 430 410); Size = (S 100 32); DialogResult = 'Cancel' } | Out-Null
    $check = { $ok.Enabled = $chk.Checked -and (($txt.Text -replace '\D', '') -eq $Code) }
    $txt.Add_TextChanged($check)
    $chk.Add_CheckedChanged($check)
    $bEmail.Add_Click({ Safe { Send-CodeEmail $Code $To } })
    $bCopy.Add_Click({ Safe { [System.Windows.Forms.Clipboard]::SetText((Format-NGCode $Code)); Msg 'Copied. Paste it into a message to your trusted person.' } })
    $bPrint.Add_Click({ Safe { Print-Code $Code $To } })
    $r = $d.ShowDialog($script:form)
    $d.Dispose()
    return ($r -eq 'OK')
}

# Asks for the code. Returns the typed code, or $null if cancelled.
function Read-Code([string]$Why) {
    $d = New-Ctl 'Form' $null @{ Text = 'NightGuard - code needed'; Size = (S 420 210); StartPosition = 'CenterParent'
        FormBorderStyle = 'FixedDialog'; MaximizeBox = $false; MinimizeBox = $false; Font = $FontUI }
    New-Ctl 'Label' $d @{ Text = "$Why`nEnter the trusted person's 8-digit code:"; Location = (P 16 14); Size = (S 380 44) } | Out-Null
    $txt = New-Ctl 'TextBox' $d @{ Location = (P 16 64); Size = (S 200 26); Font = $FontMono; MaxLength = 12; UseSystemPasswordChar = $true }
    $ok = New-Ctl 'Button' $d @{ Text = 'OK'; Location = (P 200 120); Size = (S 90 30); DialogResult = 'OK' }
    New-Ctl 'Button' $d @{ Text = 'Cancel'; Location = (P 300 120); Size = (S 90 30); DialogResult = 'Cancel' } | Out-Null
    $d.AcceptButton = $ok
    $r = $d.ShowDialog($script:form)
    $code = $txt.Text
    $d.Dispose()
    if ($r -ne 'OK') { return $null }
    return $code
}

# Returns '' when no code is needed, the code if typed, or $null if cancelled.
function Get-CodeIfNeeded([string]$Why) {
    if (-not (Get-NGTrusted)) { return '' }
    return (Read-Code $Why)
}

# ============================================================================
# Main window
# ============================================================================
$script:form = New-Ctl 'Form' $null @{ Text = 'NightGuard'; Size = (S 1170 720); StartPosition = 'CenterScreen'
    FormBorderStyle = 'FixedSingle'; MaximizeBox = $false; Font = $FontUI }

# The app is a step-by-step wizard: a list of steps on the left, one page at a time
# on the right (tab pages with their tab headers hidden), and Back / Next at the bottom.
$pnlSteps = New-Ctl 'Panel' $form @{ Location = (P 0 0); Size = (S 205 690); BackColor = [System.Drawing.Color]::FromArgb(28, 32, 52) }
New-Ctl 'Label' $pnlSteps @{ Text = 'NightGuard'; Location = (P 18 18); Size = (S 180 32); ForeColor = [System.Drawing.Color]::White
    Font = (New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Bold)) } | Out-Null
$lstSteps = New-Ctl 'ListBox' $pnlSteps @{ Location = (P 10 64); Size = (S 190 420); BorderStyle = 'None'; IntegralHeight = $false
    BackColor = [System.Drawing.Color]::FromArgb(28, 32, 52); ForeColor = [System.Drawing.Color]::White
    Font = (New-Object System.Drawing.Font('Segoe UI', 11)); ItemHeight = 34; DrawMode = 'OwnerDrawFixed' }
$tabs = New-Ctl 'TabControl' $form @{ Location = (P 215 4); Size = (S 935 616); Appearance = 'FlatButtons'; ItemSize = (S 0 1); SizeMode = 'Fixed' }
function New-Tab([string]$Title) { $t = New-Object System.Windows.Forms.TabPage $Title; $t.UseVisualStyleBackColor = $true; [void]$tabs.TabPages.Add($t); return $t }

$tStatus = New-Tab 'Welcome'
$tSched  = New-Tab 'Bedtimes'
$tWarn   = New-Tab 'Warnings'
$tAct    = New-Tab 'At bedtime'
$tScreen = New-Tab 'Game and screen time'
$tBlock  = New-Tab 'Block sites and apps'
$tTrust  = New-Tab 'Trusted person'
$tReview = New-Tab 'Review and save'
foreach ($t in $tabs.TabPages) { [void]$lstSteps.Items.Add($t.Text) }

# Draw the step list: current step highlighted, numbered
$lstSteps.Add_DrawItem({
    param($s, $e)
    if ($e.Index -lt 0) { return }
    $sel = ($e.Index -eq $tabs.SelectedIndex)
    $bg = if ($sel) { [System.Drawing.Color]::FromArgb(70, 90, 200) } else { [System.Drawing.Color]::FromArgb(28, 32, 52) }
    $br = New-Object System.Drawing.SolidBrush $bg
    $e.Graphics.FillRectangle($br, $e.Bounds); $br.Dispose()
    $txt = '{0}.  {1}' -f ($e.Index + 1), $lstSteps.Items[$e.Index]
    $fc = if ($sel) { [System.Drawing.Color]::White } else { [System.Drawing.Color]::FromArgb(190, 195, 215) }
    [System.Windows.Forms.TextRenderer]::DrawText($e.Graphics, $txt, $lstSteps.Font, (New-Object System.Drawing.Point(($e.Bounds.X + 8), ($e.Bounds.Y + 7))), $fc)
})
$lstSteps.Add_SelectedIndexChanged({ if ($lstSteps.SelectedIndex -ge 0 -and $tabs.SelectedIndex -ne $lstSteps.SelectedIndex) { $tabs.SelectedIndex = $lstSteps.SelectedIndex } })

$btnBack    = New-Ctl 'Button' $form @{ Text = '< Back'; Location = (P 735 628); Size = (S 100 40); Font = $FontRow }
$btnNext    = New-Ctl 'Button' $form @{ Text = 'Next >'; Location = (P 843 628); Size = (S 100 40); Font = $FontRowB }
$btnSave    = New-Ctl 'Button' $form @{ Text = 'Save and apply'; Location = (P 951 628); Size = (S 195 40); Font = $FontRowB }
$btnDiscard = New-Ctl 'Button' $pnlSteps @{ Text = 'Discard changes'; Location = (P 18 628); Size = (S 170 34); FlatStyle = 'Flat'
    ForeColor = [System.Drawing.Color]::White; BackColor = [System.Drawing.Color]::FromArgb(45, 50, 75) }
$DefaultHint = 'Nothing changes until you click Save. You can jump to any step on the left.'
$lblBottom  = New-Ctl 'Label' $form @{ Location = (P 222 628); Size = (S 505 44); ForeColor = $Gray; Font = $FontRow; Text = $DefaultHint }

function Update-Nav {
    $i = $tabs.SelectedIndex; $last = $tabs.TabPages.Count - 1
    $btnBack.Enabled = $i -gt 0
    $btnNext.Enabled = $i -lt $last
    $btnNext.Text = $(if ($i -eq $last - 1) { 'Review >' } else { 'Next >' })
    if ($lstSteps.SelectedIndex -ne $i) { $lstSteps.SelectedIndex = $i }
    $lstSteps.Invalidate()
    if ($tabs.SelectedTab -eq $tReview) { Update-Review }
}
$btnBack.Add_Click({ if ($tabs.SelectedIndex -gt 0) { $tabs.SelectedIndex-- } })
$btnNext.Add_Click({ if ($tabs.SelectedIndex -lt $tabs.TabPages.Count - 1) { $tabs.SelectedIndex++ } })
$tabs.Add_SelectedIndexChanged({ Update-Nav })

# Live check of the settings on screen; shows the first problem under the tabs
$script:loading = $false
function Update-Validation {
    if ($script:loading) { return }
    try {
        $errs = Test-NGConfig (Read-Form)
        if ($errs.Count -gt 0) {
            $lblBottom.ForeColor = $Red
            $lblBottom.Text = 'Fix before saving: ' + $errs[0] + $(if ($errs.Count -gt 1) { " (+$($errs.Count - 1) more)" } else { '' })
        } else {
            $lblBottom.ForeColor = $Gray
            $lblBottom.Text = $DefaultHint
        }
    } catch { }
}

# ---------------------------------------------------------------- Status tab
$txtStatus = New-Ctl 'TextBox' $tStatus @{ Location = (P 10 10); Size = (S 900 420); Multiline = $true; ReadOnly = $true
    ScrollBars = 'Vertical'; Font = $FontMono; BackColor = [System.Drawing.Color]::White }
$bRefresh  = New-Ctl 'Button' $tStatus @{ Text = 'Refresh'; Location = (P 10 445); Size = (S 100 32) }
$bPrevWarn = New-Ctl 'Button' $tStatus @{ Text = 'Preview warning'; Location = (P 120 445); Size = (S 130 32) }
$bPrevCd   = New-Ctl 'Button' $tStatus @{ Text = 'Preview countdown'; Location = (P 260 445); Size = (S 140 32) }
$bLog      = New-Ctl 'Button' $tStatus @{ Text = 'Open log'; Location = (P 410 445); Size = (S 100 32) }
$bUninst   = New-Ctl 'Button' $tStatus @{ Text = 'Uninstall'; Location = (P 810 445); Size = (S 100 32); ForeColor = $Red }

function Get-StatusText {
    $sb = New-Object System.Text.StringBuilder
    $add = { param($l) [void]$sb.AppendLine($l) }
    $cfg = Get-NGConfig
    if (-not $cfg) {
        & $add 'WELCOME TO NIGHTGUARD'
        & $add ''
        & $add 'Your bedtime bouncer. Setup takes a minute:'
        & $add ''
        & $add '  1. Click Next and pick your bedtimes.'
        & $add '  2. Go through the steps. The defaults are fine, so just click Next if unsure.'
        & $add '  3. Check the Review step and click Install.'
        & $add ''
        & $add 'Optional extras along the way: game and screen time limits, blocking'
        & $add 'distracting sites and apps, and a trusted-person code.'
        return $sb.ToString()
    }
    & $add 'BEDTIME LOCK IS ACTIVE'
    & $add ''
    $cur = Get-NGCurrentNight $cfg
    if ($cur) { & $add ("Locked now      : until {0:ddd HH\:mm}" -f $cur.End) }
    $next = Get-NGNextNight $cfg
    & $add ("Next night      : {0}" -f $(if ($next) { '{0:ddd HH\:mm} until {1:ddd HH\:mm}' -f $next.Start, $next.End } else { 'none scheduled' }))
    & $add ("At bedtime      : {0}, after a {1}s countdown, repeats every {2} min" -f $cfg.Action, $cfg.CountdownSeconds, $cfg.RecheckMinutes)
    $exp = Get-NGExpiry $cfg
    & $add ("End date        : {0}" -f $(if ($exp) { "last night $($cfg.EndDate), removes itself {0:ddd yyyy-MM-dd HH\:mm}" -f $exp } else { 'none, runs until uninstalled' }))
    $blk = Get-NGBlockedReason $cfg
    & $add ("Night mode      : {0}" -f $(if ($blk) { 'ON, settings and uninstall are locked right now' } else { "off (starts $($cfg.NightModeMinutes) min before bedtime)" }))
    $t = Get-NGTrusted
    & $add ("Trusted person  : {0}" -f $(if ($t) { "ON, changes need the code" + $(if ($t.Contact) { " ($($t.Contact))" } else { '' }) } else { 'off' }))
    if ($cfg.Blocking.Enabled) {
        $now = if (Test-NGBlockActive $cfg) { 'BLOCKING NOW' } else { 'not right now' }
        & $add ("Blocking        : {0} sites, {1} apps/folders, {2}" -f @($cfg.Blocking.Sites).Count, @($cfg.Blocking.Apps).Count, $now)
    } else { & $add 'Blocking        : off' }
    if ($cfg.ScreenTime.Enabled) {
        & $add ''
        foreach ($l in ((Get-NGUsageText $cfg) -split "`r`n")) { & $add $l }
    }
    & $add ''
    & $add 'Scheduled tasks:'
    foreach ($task in @(Get-ScheduledTask -TaskPath $NGTaskPath -ErrorAction SilentlyContinue)) {
        $info = $task | Get-ScheduledTaskInfo
        & $add ("  {0,-24} {1,-8} next: {2}" -f $task.TaskName, $task.State, $info.NextRunTime)
    }
    $log = Join-Path $NGDir 'log.txt'
    if (Test-Path $log) {
        & $add ''
        & $add 'Recent activity:'
        Get-Content $log -Tail 10 | ForEach-Object { & $add "  $_" }
    }
    return $sb.ToString()
}

function Update-Status {
    $txtStatus.Text = Get-StatusText
    $installed = [bool](Get-NGConfig)
    $btnSave.Text = if ($installed) { 'Save and apply' } else { 'Install NightGuard' }
    $bUninst.Enabled = $installed
    Update-TrustTab
}

# ---------------------------------------------------------------- Schedule tab
New-Ctl 'Label' $tSched @{ Location = (P 16 12); Size = (S 890 44); Font = $FontRow
    Text = "Tick the nights NightGuard should guard. Each night is named after the evening it starts, so a bedtime after midnight (like 12:30 AM) on Monday night means early Tuesday morning." } | Out-Null
New-Ctl 'Label' $tSched @{ Location = (P 16 58); Size = (S 600 20); ForeColor = $Gray; Text = $TimeHint } | Out-Null
New-Ctl 'Label' $tSched @{ Text = 'Night'; Location = (P 16 88); Size = (S 150 20); Font = $FontBold } | Out-Null
New-Ctl 'Label' $tSched @{ Text = 'Bedtime'; Location = (P 190 88); Size = (S 110 20); Font = $FontBold } | Out-Null
New-Ctl 'Label' $tSched @{ Text = 'Unlocks at'; Location = (P 320 88); Size = (S 110 20); Font = $FontBold } | Out-Null

$DayNames = @{ Mon = 'Monday'; Tue = 'Tuesday'; Wed = 'Wednesday'; Thu = 'Thursday'; Fri = 'Friday'; Sat = 'Saturday'; Sun = 'Sunday' }
$schedChk = @(); $schedBed = @(); $schedUnl = @(); $schedNote = @()
for ($i = 0; $i -lt 7; $i++) {
    $y = 112 + $i * 42
    $schedChk  += New-Ctl 'CheckBox' $tSched @{ Text = "$($DayNames[$NGDays[$i]]) night"; Location = (P 16 ($y + 2)); Size = (S 165 26); Font = $FontRow; Checked = $true }
    $schedBed  += New-TimePicker $tSched 190 $y '00:00'
    $schedUnl  += New-TimePicker $tSched 320 $y '06:00'
    $schedNote += New-Ctl 'Label' $tSched @{ Location = (P 445 ($y + 5)); Size = (S 160 22); ForeColor = $Gray }
}

function Update-SchedNotes {
    for ($i = 0; $i -lt 7; $i++) {
        $on = $schedChk[$i].Checked
        $schedBed[$i].Enabled = $on; $schedUnl[$i].Enabled = $on
        if (-not $on) { $schedNote[$i].Text = 'free night'; continue }
        $bt = [timespan]::Parse((Get-PickerTime $schedBed[$i])); $ut = [timespan]::Parse((Get-PickerTime $schedUnl[$i]))
        $start = if ($bt.TotalHours -lt 12) { $bt.TotalHours + 24 } else { $bt.TotalHours }
        $hours = 24 + $ut.TotalHours - $start
        $txt = if ($hours -le 0) { 'unlock must be after bedtime' } else { 'locked {0:0.#} h' -f $hours }
        if ($bt.TotalHours -lt 12) { $txt += ', early ' + $DayNames[$NGDays[($i + 1) % 7]].Substring(0, 3) }
        $schedNote[$i].Text = $txt
        $schedNote[$i].ForeColor = $(if ($hours -le 0 -or $hours -gt 16) { $Red } else { $Gray })
    }
}
for ($i = 0; $i -lt 7; $i++) {
    $schedChk[$i].Add_CheckedChanged({ Update-SchedNotes; Update-Validation })
    $schedBed[$i].Add_ValueChanged({ Update-SchedNotes; Update-Validation })
    $schedUnl[$i].Add_ValueChanged({ Update-SchedNotes; Update-Validation })
}

$grpQuick = New-Ctl 'GroupBox' $tSched @{ Text = 'Set several nights at once'; Location = (P 625 84); Size = (S 285 270); Font = $FontRow }
New-Ctl 'Label' $grpQuick @{ Text = 'Bedtime'; Location = (P 14 34); Size = (S 100 22) } | Out-Null
$pickQB = New-TimePicker $grpQuick 130 30 '00:00'
New-Ctl 'Label' $grpQuick @{ Text = 'Unlocks at'; Location = (P 14 72); Size = (S 100 22) } | Out-Null
$pickQU = New-TimePicker $grpQuick 130 68 '06:00'
$bQAll  = New-Ctl 'Button' $grpQuick @{ Text = 'Apply to every night'; Location = (P 14 112); Size = (S 255 40) }
$bQWeek = New-Ctl 'Button' $grpQuick @{ Text = 'School / work nights (Sun-Thu)'; Location = (P 14 158); Size = (S 255 40) }
$bQEnd  = New-Ctl 'Button' $grpQuick @{ Text = 'Weekend nights (Fri, Sat)'; Location = (P 14 204); Size = (S 255 40) }

function Set-QuickNights([string[]]$Days) {
    for ($i = 0; $i -lt 7; $i++) {
        if ($Days -contains $NGDays[$i]) {
            $schedChk[$i].Checked = $true
            $schedBed[$i].Value = $pickQB.Value
            $schedUnl[$i].Value = $pickQU.Value
        }
    }
    Update-SchedNotes; Update-Validation
}
$bQAll.Add_Click({ Set-QuickNights $NGDays })
$bQWeek.Add_Click({ Set-QuickNights @('Sun', 'Mon', 'Tue', 'Wed', 'Thu') })
$bQEnd.Add_Click({ Set-QuickNights @('Fri', 'Sat') })

New-Ctl 'Label' $tSched @{ Location = (P 16 412); Size = (S 890 44); ForeColor = $Gray
    Text = "Safety limits: a night can be at most 16 hours, and there must be at least 6 free hours between nights, so the PC is always usable during the day." } | Out-Null

# ---------------------------------------------------------------- Warnings tab
New-Ctl 'Label' $tWarn @{ Location = (P 16 12); Size = (S 890 44); Font = $FontRow
    Text = "Up to 6 popups before bedtime. Tick the ones you want. In the message you can use {minutes} {bedtime} {unlock} {action}, and \n for a new line." } | Out-Null
New-Ctl 'Label' $tWarn @{ Text = 'On'; Location = (P 16 66); Size = (S 40 20); Font = $FontBold } | Out-Null
New-Ctl 'Label' $tWarn @{ Text = 'Minutes before bedtime'; Location = (P 60 66); Size = (S 170 20); Font = $FontBold } | Out-Null
New-Ctl 'Label' $tWarn @{ Text = 'Sound'; Location = (P 236 66); Size = (S 60 20); Font = $FontBold } | Out-Null
New-Ctl 'Label' $tWarn @{ Text = 'Message'; Location = (P 300 66); Size = (S 200 20); Font = $FontBold } | Out-Null
$warnOn = @(); $warnMin = @(); $warnSnd = @(); $warnMsg = @(); $warnPrev = @()
for ($i = 0; $i -lt 6; $i++) {
    $y = 92 + $i * 50
    $warnOn  += New-Ctl 'CheckBox' $tWarn @{ Location = (P 20 ($y + 4)); Size = (S 30 24) }
    $warnMin += New-Ctl 'NumericUpDown' $tWarn @{ Location = (P 60 $y); Size = (S 80 28); Minimum = 1; Maximum = 360; Value = 15; Font = $FontRow }
    New-Ctl 'Label' $tWarn @{ Text = 'min'; Location = (P 145 ($y + 4)); Size = (S 40 22); Font = $FontRow } | Out-Null
    $warnSnd += New-Ctl 'CheckBox' $tWarn @{ Location = (P 246 ($y + 4)); Size = (S 30 24); Checked = $true }
    $warnMsg += New-Ctl 'TextBox' $tWarn @{ Location = (P 300 $y); Size = (S 500 28); Font = $FontRow; Text = 'Bedtime in {minutes} minutes. The PC will {action} at {bedtime}.' }
    $warnPrev += New-Ctl 'Button' $tWarn @{ Text = 'Preview'; Location = (P 810 ($y - 1)); Size = (S 95 32); Tag = $i }
}
function Update-WarnRows {
    for ($i = 0; $i -lt 6; $i++) {
        $on = $warnOn[$i].Checked
        $warnMin[$i].Enabled = $on; $warnSnd[$i].Enabled = $on; $warnMsg[$i].Enabled = $on; $warnPrev[$i].Enabled = $on
    }
}
for ($i = 0; $i -lt 6; $i++) {
    $warnOn[$i].Add_CheckedChanged({ Update-WarnRows; Update-Validation })
    $warnMin[$i].Add_ValueChanged({ Update-Validation })
}

# ---------------------------------------------------------------- Bedtime action tab
New-Ctl 'Label' $tAct @{ Text = 'At bedtime the PC will:'; Location = (P 10 16); Size = (S 200 20); Font = $FontBold } | Out-Null
$cboAction = New-Ctl 'ComboBox' $tAct @{ Location = (P 220 13); Size = (S 160 24); DropDownStyle = 'DropDownList' }
foreach ($a in $NGActions) { [void]$cboAction.Items.Add($a) }
$lblActDesc = New-Ctl 'Label' $tAct @{ Location = (P 400 16); Size = (S 430 40); ForeColor = [System.Drawing.Color]::DimGray }
$ActDesc = @{
    Shutdown  = 'Strongest. Closes everything; unsaved work is lost. Can wake a sleeping PC to shut it down.'
    Sleep     = 'Medium. Quick to resume, but it goes back to sleep every few minutes during the night.'
    Hibernate = 'Strong. Saves open apps to disk. If hibernate is off on this PC, it shuts down instead.'
    Lock      = 'Weakest. Locks the screen, again every few minutes, but you can keep signing back in.'
    SignOut   = 'Medium. Closes all your apps and signs you out, again if you sign back in.'
}
$cboAction.Add_SelectedIndexChanged({ $lblActDesc.Text = $ActDesc[[string]$cboAction.SelectedItem] })

function New-Num($Parent, [string]$Label, [int]$Y, [int]$Min, [int]$Max, [string]$Hint) {
    New-Ctl 'Label' $Parent @{ Text = $Label; Location = (P 10 ($Y + 3)); Size = (S 200 20) } | Out-Null
    $n = New-Ctl 'NumericUpDown' $Parent @{ Location = (P 220 $Y); Size = (S 80 24); Minimum = $Min; Maximum = $Max }
    New-Ctl 'Label' $Parent @{ Text = $Hint; Location = (P 310 ($Y + 3)); Size = (S 520 20); ForeColor = [System.Drawing.Color]::DimGray } | Out-Null
    return $n
}
$numCountdown = New-Num $tAct 'Countdown (seconds)' 70 15 600 'Notice before it happens (15 to 600 seconds).'
$numRecheck   = New-Num $tAct 'Repeat every (minutes)' 105 1 60 'During the night it repeats, so switching the PC back on does not help.'
$numNight     = New-Num $tAct 'Night mode (minutes)' 140 0 240 'Settings and uninstall are locked from this long before bedtime until unlock.'

$chkEnd = New-Ctl 'CheckBox' $tAct @{ Text = 'Stop after an end date (last locked night):'; Location = (P 10 190); Size = (S 300 24) }
$dtpEnd = New-Ctl 'DateTimePicker' $tAct @{ Location = (P 320 190); Size = (S 160 24); Format = 'Custom'; CustomFormat = 'ddd yyyy-MM-dd'; Enabled = $false }
$chkEnd.Add_CheckedChanged({ $dtpEnd.Enabled = $chkEnd.Checked })
New-Ctl 'Label' $tAct @{ Location = (P 10 222); Size = (S 820 40); ForeColor = [System.Drawing.Color]::DimGray
    Text = 'After that night, NightGuard removes itself completely (tasks, blocks and files).' } | Out-Null

# ---------------------------------------------------------------- Blocking tab
$chkBlock = New-Ctl 'CheckBox' $tBlock @{ Text = 'Turn on site and app blocking'; Location = (P 10 10); Size = (S 300 24); Font = $FontBold }
$chkBlockBed = New-Ctl 'CheckBox' $tBlock @{ Text = 'Also block during bedtime hours'; Location = (P 320 10); Size = (S 300 24) }

New-Ctl 'Label' $tBlock @{ Text = 'Websites (one per line):'; Location = (P 10 44); Size = (S 260 20) } | Out-Null
$txtSites = New-Ctl 'TextBox' $tBlock @{ Location = (P 10 66); Size = (S 250 330); Multiline = $true; ScrollBars = 'Vertical'; Font = $FontMono }
$bPreset = New-Ctl 'Button' $tBlock @{ Text = 'Add social media list'; Location = (P 10 402); Size = (S 250 30) }

New-Ctl 'Label' $tBlock @{ Text = 'Apps (their internet is blocked):'; Location = (P 280 44); Size = (S 300 20) } | Out-Null
$lstApps = New-Ctl 'ListBox' $tBlock @{ Location = (P 280 66); Size = (S 490 120); HorizontalScrollbar = $true }
$bAddExe = New-Ctl 'Button' $tBlock @{ Text = 'Add program...'; Location = (P 780 66); Size = (S 130 32) }
$bAddDir = New-Ctl 'Button' $tBlock @{ Text = 'Add folder...'; Location = (P 780 104); Size = (S 130 32) }
$bDelApp = New-Ctl 'Button' $tBlock @{ Text = 'Remove'; Location = (P 780 142); Size = (S 130 32) }

New-Ctl 'Label' $tBlock @{ Text = 'Block at these times (a slot like 10 PM to 2 AM crosses midnight):'; Location = (P 280 196); Size = (S 630 20) } | Out-Null
$slotOn = @(); $slotDays = @(); $slotFrom = @(); $slotTo = @()
$x0 = 280
New-Ctl 'Label' $tBlock @{ Text = 'On'; Location = (P $x0 220); Size = (S 30 18); Font = $FontBold } | Out-Null
for ($d = 0; $d -lt 7; $d++) { New-Ctl 'Label' $tBlock @{ Text = $NGDays[$d]; Location = (P ($x0 + 40 + $d * 42) 220); Size = (S 40 18); Font = $FontBold } | Out-Null }
New-Ctl 'Label' $tBlock @{ Text = 'From'; Location = (P ($x0 + 340) 220); Size = (S 60 18); Font = $FontBold } | Out-Null
New-Ctl 'Label' $tBlock @{ Text = 'To'; Location = (P ($x0 + 482) 220); Size = (S 60 18); Font = $FontBold } | Out-Null
for ($i = 0; $i -lt 4; $i++) {
    $y = 242 + $i * 38
    $slotOn += New-Ctl 'CheckBox' $tBlock @{ Location = (P $x0 ($y + 3)); Size = (S 28 24) }
    $days = @()
    for ($d = 0; $d -lt 7; $d++) { $days += New-Ctl 'CheckBox' $tBlock @{ Location = (P ($x0 + 44 + $d * 42) ($y + 3)); Size = (S 28 24); Checked = ($d -lt 5) } }
    $slotDays += , $days
    $slotFrom += New-TimePicker $tBlock ($x0 + 340) $y '09:00'
    New-Ctl 'Label' $tBlock @{ Text = 'to'; Location = (P ($x0 + 457) ($y + 5)); Size = (S 22 20) } | Out-Null
    $slotTo += New-TimePicker $tBlock ($x0 + 482) $y '17:00'
}
foreach ($c in $slotOn) { $c.Add_CheckedChanged({ Update-Validation }) }

New-Ctl 'Label' $tBlock @{ Location = (P 280 400); Size = (S 630 90); ForeColor = [System.Drawing.Color]::DimGray
    Text = "Sites are blocked in a marked section of the Windows hosts file; apps with Windows Firewall rules named 'NightGuard'. Both are removed automatically when the time slot ends, on uninstall, and if anything goes wrong. Browsers with 'secure DNS' turned on can get around site blocking (see the README). Blocking an app cuts its internet; it does not close it." } | Out-Null

$bPreset.Add_Click({
    $have = @($txtSites.Lines | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
    $add = @($NGSocialPreset | Where-Object { $have -notcontains $_ })
    $txtSites.Lines = @($have + $add)
})
$bAddExe.Add_Click({
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Filter = 'Programs (*.exe)|*.exe'
    if ($dlg.ShowDialog($form) -eq 'OK') {
        if (Test-NGProtectedPath $dlg.FileName) { Msg "That program is part of Windows and can't be blocked." 'Warning' }
        elseif (-not $lstApps.Items.Contains($dlg.FileName)) { [void]$lstApps.Items.Add($dlg.FileName) }
    }
})
$bAddDir.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Every program inside this folder will be blocked (for example a Steam or Discord folder).'
    if ($dlg.ShowDialog($form) -eq 'OK') {
        if (Test-NGProtectedPath $dlg.SelectedPath) { Msg "That folder is part of Windows and can't be blocked." 'Warning' }
        elseif (-not $lstApps.Items.Contains($dlg.SelectedPath)) { [void]$lstApps.Items.Add($dlg.SelectedPath) }
    }
})
$bDelApp.Add_Click({ if ($lstApps.SelectedIndex -ge 0) { $lstApps.Items.RemoveAt($lstApps.SelectedIndex) } })

# ---------------------------------------------------------------- Trusted person tab
New-Ctl 'Label' $tTrust @{ Location = (P 10 10); Size = (S 820 80); Font = (New-Object System.Drawing.Font('Segoe UI', 10)); Text = @'
Trusted-person mode makes NightGuard much harder to undo. You give an 8-digit code to someone you trust, such as a parent, partner or friend. After that, changing any setting or uninstalling needs that code.
'@ } | Out-Null
$lblTrustState = New-Ctl 'Label' $tTrust @{ Location = (P 10 96); Size = (S 820 26); Font = $FontBig }
New-Ctl 'Label' $tTrust @{ Text = "Trusted person's email (optional):"; Location = (P 10 140); Size = (S 220 20) } | Out-Null
$txtContact = New-Ctl 'TextBox' $tTrust @{ Location = (P 240 137); Size = (S 300 24) }
$bTrustOn  = New-Ctl 'Button' $tTrust @{ Text = 'Turn on and create code...'; Location = (P 10 180); Size = (S 220 34); Font = $FontBold }
$bNewCode  = New-Ctl 'Button' $tTrust @{ Text = 'Create a new code...'; Location = (P 240 180); Size = (S 180 34) }
$bTrustOff = New-Ctl 'Button' $tTrust @{ Text = 'Turn off...'; Location = (P 430 180); Size = (S 120 34) }
New-Ctl 'Label' $tTrust @{ Location = (P 10 236); Size = (S 820 150); ForeColor = $Red; Font = $FontBold; Text = @'
WARNING: THERE IS NO WAY TO RECOVER A LOST CODE.
If the code is lost, settings and uninstall stay locked for good. The code is shown only once and this PC keeps only a scrambled copy.
Wrong codes lock the code box for 15 minutes after 5 tries, and longer after each further try.

(NightGuard still never stops the PC working outside bedtime hours, and the end date still removes it.)
'@ } | Out-Null

function Update-TrustTab {
    $installed = [bool](Get-NGConfig)
    $t = Get-NGTrusted
    if ($t) {
        $lblTrustState.Text = 'Trusted-person mode is ON'
        $lblTrustState.ForeColor = [System.Drawing.Color]::DarkGreen
        if ($t.Contact) { $txtContact.Text = $t.Contact }
    } else {
        $lblTrustState.Text = $(if ($installed) { 'Trusted-person mode is off' } else { 'Install NightGuard first (Install button below)' })
        $lblTrustState.ForeColor = [System.Drawing.Color]::DimGray
    }
    $bTrustOn.Enabled  = $installed -and -not $t
    $bNewCode.Enabled  = [bool]$t
    $bTrustOff.Enabled = [bool]$t
}

function Test-ContactOk {
    $c = $txtContact.Text.Trim()
    if ($c -and $c -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { Msg 'That email address looks wrong.' 'Warning'; return $false }
    return $true
}

$bTrustOn.Add_Click({ Safe {
    if (-not (Test-ContactOk)) { return }
    if (-not (Confirm-Ask "Save any settings changes first. After this, every change needs the code.`n`nContinue?")) { return }
    if (-not (Show-CodeWarning)) { return }
    $code = New-NGCode
    if (Show-NewCode $code $txtContact.Text.Trim()) {
        Set-NGTrusted $code $txtContact.Text.Trim()
        Write-NGLog 'trusted-person mode turned on'
        Msg 'Trusted-person mode is on. From now on, changes and uninstall need the code.'
    } else {
        Msg 'Cancelled. Trusted-person mode is still off, and that code was thrown away.'
    }
    Update-Status
} })

$bNewCode.Add_Click({ Safe {
    $blk = Get-NGBlockedReason (Get-NGConfig); if ($blk) { throw $blk }
    if (-not (Test-ContactOk)) { return }
    $old = Read-Code 'To create a new code, first enter the current one.'
    if ($null -eq $old) { return }
    Assert-NGCode $old
    if (-not (Show-CodeWarning)) { return }
    $code = New-NGCode
    if (Show-NewCode $code $txtContact.Text.Trim()) {
        Set-NGTrusted $code $txtContact.Text.Trim()
        Write-NGLog 'trusted-person code changed'
        Msg 'The new code is active. The old code no longer works.'
    } else {
        Msg 'Cancelled. The old code still works.'
    }
    Update-Status
} })

$bTrustOff.Add_Click({ Safe {
    $blk = Get-NGBlockedReason (Get-NGConfig); if ($blk) { throw $blk }
    $code = Read-Code 'Turning off trusted-person mode needs the code.'
    if ($null -eq $code) { return }
    Assert-NGCode $code
    Remove-NGTrusted
    Write-NGLog 'trusted-person mode turned off'
    Msg 'Trusted-person mode is off.'
    Update-Status
} })

# ---------------------------------------------------------------- Screen time tab
$chkST = New-Ctl 'CheckBox' $tScreen @{ Text = 'Turn on daily game and screen time limits'; Location = (P 10 10); Size = (S 400 24); Font = $FontBold }
$lblUsage = New-Ctl 'Label' $tScreen @{ Location = (P 420 8); Size = (S 410 50); Font = $FontMono }

function New-STNum([string]$Label, [int]$X, [int]$Y, [int]$Min, [int]$Max) {
    New-Ctl 'Label' $tScreen @{ Text = $Label; Location = (P $X ($Y + 3)); Size = (S 150 20) } | Out-Null
    return (New-Ctl 'NumericUpDown' $tScreen @{ Location = (P ($X + 155) $Y); Size = (S 70 24); Minimum = $Min; Maximum = $Max })
}
New-Ctl 'Label' $tScreen @{ Text = 'Minutes per day (0 = no limit):'; Location = (P 10 48); Size = (S 400 20); ForeColor = [System.Drawing.Color]::DimGray } | Out-Null
$numGWd = New-STNum 'Games, weekdays' 10 72 0 1440
$numGWe = New-STNum 'Games, weekends' 10 102 0 1440
$numSWd = New-STNum 'Screen, weekdays' 260 72 0 1440
$numSWe = New-STNum 'Screen, weekends' 260 102 0 1440
$numReset = New-STNum 'New day starts at (hour)' 520 72 0 23
$numGrace = New-STNum 'Shutdown notice (min)' 520 102 1 10

New-Ctl 'Label' $tScreen @{ Text = 'When screen time runs out:'; Location = (P 10 143); Size = (S 160 20) } | Out-Null
$cboST = New-Ctl 'ComboBox' $tScreen @{ Location = (P 175 140); Size = (S 110 24); DropDownStyle = 'DropDownList' }
foreach ($a in $NGScreenActions) { [void]$cboST.Items.Add($a) }
New-Ctl 'Label' $tScreen @{ Text = 'Warn at (minutes left):'; Location = (P 300 143); Size = (S 140 20) } | Out-Null
$txtSTWarn = New-Ctl 'TextBox' $tScreen @{ Location = (P 445 140); Size = (S 100 24) }
$chkLaunch = New-Ctl 'CheckBox' $tScreen @{ Text = 'Also close game launchers'; Location = (P 560 140); Size = (S 260 24) }

function New-STList([string]$Label, [int]$X, [int]$W) {
    New-Ctl 'Label' $tScreen @{ Text = $Label; Location = (P $X 176); Size = (S $W 20) } | Out-Null
    return (New-Ctl 'TextBox' $tScreen @{ Location = (P $X 198); Size = (S $W 250); Multiline = $true; ScrollBars = 'Both'; WordWrap = $false; Font = $FontMono })
}
$txtGFolders = New-STList 'Game folders (one per line)' 10 290
$txtGProcs   = New-STList 'Extra games (program names)' 310 170
$txtIgnore   = New-STList 'Never count as games' 490 165
$txtLaunch   = New-STList 'Launchers' 665 165
$bAddGF = New-Ctl 'Button' $tScreen @{ Text = 'Add folder...'; Location = (P 10 454); Size = (S 110 28) }
$bUsage = New-Ctl 'Button' $tScreen @{ Text = 'Refresh usage'; Location = (P 130 454); Size = (S 110 28) }
New-Ctl 'Label' $tScreen @{ Location = (P 250 452); Size = (S 580 36); ForeColor = [System.Drawing.Color]::DimGray
    Text = "Anything running from a game folder counts as a game. Program names are as in Task Manager > Details, without .exe. Windows' own programs are never closed." } | Out-Null

$bAddGF.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Pick a folder where games are installed.'
    if ($dlg.ShowDialog($form) -eq 'OK') {
        if (Test-NGProtectedPath $dlg.SelectedPath) { Msg "That folder is part of Windows and can't be a game folder." 'Warning' }
        else { $txtGFolders.Lines = @(@($txtGFolders.Lines | Where-Object { $_.Trim() }) + $dlg.SelectedPath) }
    }
})
$bUsage.Add_Click({ Safe { $c = Get-NGConfig; $lblUsage.Text = $(if ($c) { Get-NGUsageText $c } else { '' }) } })

# ---------------------------------------------------------------- Review page
New-Ctl 'Label' $tReview @{ Text = 'Review and save'; Location = (P 16 12); Size = (S 600 30); Font = $FontBig } | Out-Null
New-Ctl 'Label' $tReview @{ Location = (P 16 46); Size = (S 890 24); Font = $FontRow
    Text = 'Here is everything NightGuard will do. Go back to any step to change it, then click Save.' } | Out-Null
$txtReview = New-Ctl 'TextBox' $tReview @{ Location = (P 16 78); Size = (S 895 500); Multiline = $true; ReadOnly = $true; ScrollBars = 'Vertical'
    Font = (New-Object System.Drawing.Font('Consolas', 10.5)); BackColor = [System.Drawing.Color]::White }

function Update-Review {
    $sb = New-Object System.Text.StringBuilder
    $add = { param($l) [void]$sb.AppendLine($l) }
    try { $cfg = Read-Form } catch { $txtReview.Text = $_.Exception.Message; return }
    $errs = Test-NGConfig $cfg
    if ($errs.Count -gt 0) {
        & $add 'PLEASE FIX THESE FIRST:'
        foreach ($e in $errs) { & $add "  - $e" }
        & $add ''
    }
    & $add 'BEDTIMES'
    for ($i = 0; $i -lt 7; $i++) {
        $s = $cfg.Schedule.($NGDays[$i])
        $name = ('{0} night' -f $DayNames[$NGDays[$i]]).PadRight(17)
        if ($s.Enabled) { & $add ("  {0} {1} until {2}" -f $name, (Format-LocalTime $s.Bedtime), (Format-LocalTime $s.Unlock)) }
        else { & $add "  $name free" }
    }
    & $add ''
    & $add 'WARNINGS'
    $ws = @($cfg.Warnings | Where-Object { $_ })
    if ($ws.Count -eq 0) { & $add '  none' }
    foreach ($w in $ws) { & $add ("  {0} min before bedtime{1}" -f $w.MinutesBefore, $(if ($w.Sound) { ', with sound' } else { '' })) }
    & $add ''
    & $add 'AT BEDTIME'
    & $add ("  {0} after a {1} second countdown, repeating every {2} min through the night" -f $cfg.Action, $cfg.CountdownSeconds, $cfg.RecheckMinutes)
    & $add ("  Settings locked from {0} min before bedtime until unlock" -f $cfg.NightModeMinutes)
    & $add ("  End date: {0}" -f $(if ($cfg.EndDate) { "last night is $($cfg.EndDate), then NightGuard removes itself" } else { 'none' }))
    & $add ''
    & $add 'GAME AND SCREEN TIME'
    $st = $cfg.ScreenTime
    if ($st.Enabled) {
        $f = { param($m) if ([int]$m -le 0) { 'no limit' } else { Format-NGMinutes $m } }
        & $add ("  Games : {0} weekdays, {1} weekends" -f (& $f $st.GameMinutesWeekday), (& $f $st.GameMinutesWeekend))
        & $add ("  Screen: {0} weekdays, {1} weekends, then {2}" -f (& $f $st.ScreenMinutesWeekday), (& $f $st.ScreenMinutesWeekend), $st.LimitAction)
        & $add ("  New day starts at {0}:00" -f $st.ResetHour)
    } else { & $add '  off' }
    & $add ''
    & $add 'BLOCKING'
    $b = $cfg.Blocking
    if ($b.Enabled) {
        & $add ("  {0} sites, {1} apps/folders" -f @($b.Sites).Count, @($b.Apps).Count)
        if ($b.DuringBedtime) { & $add '  during bedtime hours' }
        foreach ($w in @($b.Windows)) { & $add ("  {0}: {1} to {2}" -f (@($w.Days) -join ' '), (Format-LocalTime $w.From), (Format-LocalTime $w.To)) }
    } else { & $add '  off' }
    & $add ''
    & $add 'TRUSTED PERSON'
    & $add $(if (Get-NGTrusted) { '  ON, saving needs the code' } else { '  off (set it up on the Trusted person step, after installing)' })
    $txtReview.Text = $sb.ToString()
}

# ============================================================================
# Moving settings between the form and the config
# ============================================================================
function Load-Form($Cfg) {
    $script:loading = $true
    for ($i = 0; $i -lt 7; $i++) {
        $s = $Cfg.Schedule.($NGDays[$i])
        $schedChk[$i].Checked = [bool]$s.Enabled
        Set-PickerTime $schedBed[$i] ([string]$s.Bedtime)
        Set-PickerTime $schedUnl[$i] ([string]$s.Unlock)
    }
    Update-SchedNotes
    $ws = @($Cfg.Warnings | Where-Object { $_ })
    $defaultMins = @(30, 5, 60, 15, 10, 2)
    for ($i = 0; $i -lt 6; $i++) {
        if ($i -lt $ws.Count) {
            $warnOn[$i].Checked = $true
            $warnMin[$i].Value = [math]::Min(360, [math]::Max(1, [int]$ws[$i].MinutesBefore))
            $warnSnd[$i].Checked = [bool]$ws[$i].Sound
            $warnMsg[$i].Text = [string]$ws[$i].Message
        } else {
            $warnOn[$i].Checked = $false
            $used = @($ws | ForEach-Object { [int]$_.MinutesBefore })
            $warnMin[$i].Value = @($defaultMins + (1..360) | Where-Object { $used -notcontains $_ })[$i - $ws.Count]
            $warnSnd[$i].Checked = $true
            $warnMsg[$i].Text = 'Bedtime in {minutes} minutes. The PC will {action} at {bedtime}.'
        }
    }
    Update-WarnRows
    $cboAction.SelectedItem = [string]$Cfg.Action
    $numCountdown.Value = [math]::Min(600, [math]::Max(15, [int]$Cfg.CountdownSeconds))
    $numRecheck.Value   = [math]::Min(60, [math]::Max(1, [int]$Cfg.RecheckMinutes))
    $numNight.Value     = [math]::Min(240, [math]::Max(0, [int]$Cfg.NightModeMinutes))
    $end = Get-NGEndDate $Cfg
    $today = (Get-Date).Date
    $dtpEnd.MinDate = $(if ($end -and $end -lt $today) { $end } else { $today })
    $dtpEnd.Value = $(if ($end) { $end } else { $today.AddDays(30) })
    $chkEnd.Checked = [bool]$end
    $dtpEnd.Enabled = [bool]$end
    $b = $Cfg.Blocking
    $chkBlock.Checked = [bool]$b.Enabled
    $chkBlockBed.Checked = [bool]$b.DuringBedtime
    $txtSites.Lines = @($b.Sites | Where-Object { $_ })
    $lstApps.Items.Clear()
    foreach ($a in @($b.Apps | Where-Object { $_ })) { [void]$lstApps.Items.Add([string]$a) }
    $st = $Cfg.ScreenTime
    $chkST.Checked = [bool]$st.Enabled
    $numGWd.Value = [math]::Min(1440, [math]::Max(0, [int]$st.GameMinutesWeekday))
    $numGWe.Value = [math]::Min(1440, [math]::Max(0, [int]$st.GameMinutesWeekend))
    $numSWd.Value = [math]::Min(1440, [math]::Max(0, [int]$st.ScreenMinutesWeekday))
    $numSWe.Value = [math]::Min(1440, [math]::Max(0, [int]$st.ScreenMinutesWeekend))
    $numReset.Value = [math]::Min(23, [math]::Max(0, [int]$st.ResetHour))
    $numGrace.Value = [math]::Min(10, [math]::Max(1, [int]$st.ShutdownGraceMinutes))
    $cboST.SelectedItem = [string]$st.LimitAction
    $txtSTWarn.Text = (@($st.WarnAtMinutesLeft) -join ', ')
    $chkLaunch.Checked = [bool]$st.CloseLaunchers
    $txtGFolders.Lines = @($st.GameFolders | Where-Object { $_ })
    $txtGProcs.Lines   = @($st.GameProcesses | Where-Object { $_ })
    $txtIgnore.Lines   = @($st.IgnoreProcesses | Where-Object { $_ })
    $txtLaunch.Lines   = @($st.LauncherProcesses | Where-Object { $_ })
    $lblUsage.Text = $(if (Get-NGConfig) { Get-NGUsageText $Cfg } else { '' })
    $wins = @($b.Windows | Where-Object { $_ })
    for ($i = 0; $i -lt 4; $i++) {
        if ($i -lt $wins.Count) {
            $slotOn[$i].Checked = $true
            for ($d = 0; $d -lt 7; $d++) { $slotDays[$i][$d].Checked = (@($wins[$i].Days) -contains $NGDays[$d]) }
            Set-PickerTime $slotFrom[$i] ([string]$wins[$i].From)
            Set-PickerTime $slotTo[$i] ([string]$wins[$i].To)
        } else {
            $slotOn[$i].Checked = $false
        }
    }
    $script:loading = $false
    Update-Validation
}

function Read-Form {
    $sched = [ordered]@{}
    for ($i = 0; $i -lt 7; $i++) {
        $sched[$NGDays[$i]] = [pscustomobject]@{ Enabled = $schedChk[$i].Checked; Bedtime = Get-PickerTime $schedBed[$i]; Unlock = Get-PickerTime $schedUnl[$i] }
    }
    $warn = @()
    for ($i = 0; $i -lt 6; $i++) {
        if (-not $warnOn[$i].Checked) { continue }
        $warn += [pscustomobject]@{ MinutesBefore = [int]$warnMin[$i].Value; Sound = $warnSnd[$i].Checked; Message = $warnMsg[$i].Text }
    }
    $wins = @()
    for ($i = 0; $i -lt 4; $i++) {
        if (-not $slotOn[$i].Checked) { continue }
        $days = @(); for ($d = 0; $d -lt 7; $d++) { if ($slotDays[$i][$d].Checked) { $days += $NGDays[$d] } }
        $wins += [pscustomobject]@{ Days = $days; From = Get-PickerTime $slotFrom[$i]; To = Get-PickerTime $slotTo[$i] }
    }
    $cfg = [pscustomobject]@{
        Version = 3
        Schedule = [pscustomobject]$sched
        Warnings = $warn
        Action = [string]$cboAction.SelectedItem
        CountdownSeconds = [int]$numCountdown.Value
        RecheckMinutes = [int]$numRecheck.Value
        NightModeMinutes = [int]$numNight.Value
        EndDate = $(if ($chkEnd.Checked) { $dtpEnd.Value.Date.ToString('yyyy-MM-dd') } else { $null })
        Blocking = [pscustomobject]@{
            Enabled = $chkBlock.Checked
            DuringBedtime = $chkBlockBed.Checked
            Sites = @($txtSites.Lines | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
            Apps = @($lstApps.Items | ForEach-Object { [string]$_ })
            Windows = $wins
        }
        ScreenTime = [pscustomobject]@{
            Enabled = $chkST.Checked
            ResetHour = [int]$numReset.Value
            GameMinutesWeekday = [int]$numGWd.Value; GameMinutesWeekend = [int]$numGWe.Value
            ScreenMinutesWeekday = [int]$numSWd.Value; ScreenMinutesWeekend = [int]$numSWe.Value
            WarnAtMinutesLeft = @($txtSTWarn.Text -split '[,;\s]+' | Where-Object { $_ } | ForEach-Object { $n = 0; if ([int]::TryParse($_, [ref]$n)) { $n } else { -1 } })
            LimitAction = [string]$cboST.SelectedItem
            ShutdownGraceMinutes = [int]$numGrace.Value
            CloseLaunchers = $chkLaunch.Checked
            GameFolders = @($txtGFolders.Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            GameProcesses = @($txtGProcs.Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            IgnoreProcesses = @($txtIgnore.Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            LauncherProcesses = @($txtLaunch.Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        }
    }
    return (Copy-NGConfig $cfg)
}

function Get-StartingConfig {
    $cfg = Get-NGConfig
    if (-not $cfg) {
        $legacy = Join-Path $NGLegacyDir 'config.json'   # carry over an old Bedtime Lock install
        if (Test-Path $legacy) { try { $cfg = Read-NGConfigFile $legacy } catch { } }
    }
    if (-not $cfg) { $cfg = Copy-NGConfig (New-NGDefaultConfig) }
    if (-not $cfg.ScreenTime.Enabled) {
        $oldSL = Get-NGOldScreenLimits   # carry over the old Screen Limits add-on
        if ($oldSL) { $cfg.ScreenTime = $oldSL; $cfg = Copy-NGConfig $cfg }
    }
    return $cfg
}

function Reload-Form {
    $cfg = Get-StartingConfig
    Load-Form $cfg
    Update-Status
}

# ============================================================================
# Buttons
# ============================================================================
$btnSave.Add_Click({ Safe {
    $cfg = Read-Form
    $errs = Test-NGConfig $cfg
    if ($errs.Count -gt 0) { Msg ("Please fix these first:`n`n- " + ($errs -join "`n- ")) 'Warning'; return }
    if ($cfg.EndDate -and (Test-NGExpired $cfg)) { Msg 'The end date has already passed.' 'Warning'; return }
    $blk = Get-NGBlockedReason (Get-NGConfig); if ($blk) { throw $blk }
    $code = Get-CodeIfNeeded 'Saving changes needs the code.'
    if ($null -eq $code) { return }
    $tmp = Join-Path $env:TEMP 'nightguard-settings.json'
    Set-Content -Path $tmp -Value (ConvertTo-NGJson $cfg) -Encoding UTF8
    $form.Cursor = 'WaitCursor'
    try { & (Join-Path $PSScriptRoot 'install.ps1') -ConfigPath $tmp -Code $code -NoPause -ThrowOnError | Out-Null }
    finally { $form.Cursor = 'Default'; Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
    Reload-Form
    Msg 'Saved. Your settings are active.'
} })

$btnDiscard.Add_Click({ Safe { Reload-Form } })
$bRefresh.Add_Click({ Safe { Update-Status } })

function Invoke-Preview([string]$Script, [string[]]$ScriptArgs, $Cfg = $null) {
    if (-not $Cfg) { $Cfg = Read-Form }
    # Preview with the settings currently on screen
    $tmp = Join-Path $env:TEMP 'nightguard-preview'
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'common.ps1'), (Join-Path $PSScriptRoot $Script) -Destination $tmp -Force
    Set-Content -Path (Join-Path $tmp 'config.json') -Value (ConvertTo-NGJson $Cfg) -Encoding UTF8
    Start-Process powershell.exe -ArgumentList (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$(Join-Path $tmp $Script)`"") + $ScriptArgs)
}
$bPrevWarn.Add_Click({ Safe { Invoke-Preview 'warn.ps1' @('-Preview', '-Index', '1') } })
$bPrevCd.Add_Click({ Safe { Invoke-Preview 'session.ps1' @('-Preview') } })
foreach ($wb in $warnPrev) {
    $wb.Add_Click({ param($sender, $e) Safe {
        $i = [int]$sender.Tag
        $cfg = Read-Form
        $cfg.Warnings = @([pscustomobject]@{ MinutesBefore = [int]$warnMin[$i].Value; Sound = $warnSnd[$i].Checked; Message = $warnMsg[$i].Text })
        Invoke-Preview 'warn.ps1' @('-Preview', '-Index', '1') $cfg
    } })
}
$bLog.Add_Click({ Safe {
    $log = Join-Path $NGDir 'log.txt'
    if (Test-Path $log) { Start-Process notepad.exe -ArgumentList "`"$log`"" } else { Msg 'Nothing logged yet.' }
} })


$bUninst.Add_Click({ Safe {
    $blk = Get-NGBlockedReason (Get-NGConfig); if ($blk) { throw $blk }
    if (-not (Confirm-Ask 'Really remove NightGuard? This removes its tasks, blocks and files.')) { return }
    $code = Get-CodeIfNeeded 'Uninstalling needs the code.'
    if ($null -eq $code) { return }
    & (Join-Path $PSScriptRoot 'uninstall.ps1') -Code $code -NoPause -ThrowOnError | Out-Null
    Msg 'NightGuard has been removed.'
    $form.Close()
} })

# Refresh the status tab every 30 seconds while it's showing
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 30000
$timer.Add_Tick({ if ($tabs.SelectedTab -eq $tStatus) { try { $txtStatus.Text = Get-StatusText } catch { } } })
$timer.Start()

try {
    Reload-Form
    Update-Nav
    [void]$form.ShowDialog()
} catch {
    [void][System.Windows.Forms.MessageBox]::Show("NightGuard could not start: $($_.Exception.Message)`n`nYou can use TextMenu.cmd instead.", 'NightGuard', 'OK', 'Error')
} finally {
    $timer.Stop()
}
