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
$Red      = [System.Drawing.Color]::FromArgb(200, 30, 30)

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

function Add-Col($Grid, [string]$Type, [string]$Name, [string]$Header, [int]$Width, [bool]$ReadOnly = $false) {
    $c = New-Object "System.Windows.Forms.DataGridView${Type}Column"
    $c.Name = $Name; $c.HeaderText = $Header; $c.Width = $Width; $c.ReadOnly = $ReadOnly
    $c.SortMode = 'NotSortable'
    [void]$Grid.Columns.Add($c)
    return $c
}

function New-Grid($Parent, $Location, $Size, [bool]$AddRows) {
    return New-Ctl 'DataGridView' $Parent @{
        Location = $Location; Size = $Size; AllowUserToAddRows = $AddRows; AllowUserToDeleteRows = $AddRows
        RowHeadersVisible = $false; AllowUserToResizeRows = $false; SelectionMode = 'FullRowSelect'
        MultiSelect = $false; BackgroundColor = [System.Drawing.SystemColors]::Window; EditMode = 'EditOnEnter'
    }
}

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
$script:form = New-Ctl 'Form' $null @{ Text = 'NightGuard'; Size = (S 880 660); StartPosition = 'CenterScreen'
    FormBorderStyle = 'FixedSingle'; MaximizeBox = $false; Font = $FontUI }
$tabs = New-Ctl 'TabControl' $form @{ Location = (P 8 8); Size = (S 850 560) }
function New-Tab([string]$Title) { $t = New-Object System.Windows.Forms.TabPage $Title; $t.UseVisualStyleBackColor = $true; [void]$tabs.TabPages.Add($t); return $t }

$tStatus = New-Tab 'Status'
$tSched  = New-Tab 'Schedule'
$tWarn   = New-Tab 'Warnings'
$tAct    = New-Tab 'Bedtime action'
$tBlock  = New-Tab 'Block sites and apps'
$tTrust  = New-Tab 'Trusted person'
$tScreen = New-Tab 'Screen time'

$btnSave    = New-Ctl 'Button' $form @{ Text = 'Save and apply'; Location = (P 588 578); Size = (S 140 34); Font = $FontBold }
$btnDiscard = New-Ctl 'Button' $form @{ Text = 'Discard changes'; Location = (P 736 578); Size = (S 120 34) }
$lblBottom  = New-Ctl 'Label' $form @{ Location = (P 12 586); Size = (S 560 24); ForeColor = [System.Drawing.Color]::DimGray
    Text = 'Changes on the settings tabs take effect when you click Save and apply.' }

# ---------------------------------------------------------------- Status tab
$txtStatus = New-Ctl 'TextBox' $tStatus @{ Location = (P 10 10); Size = (S 820 420); Multiline = $true; ReadOnly = $true
    ScrollBars = 'Vertical'; Font = $FontMono; BackColor = [System.Drawing.Color]::White }
$bRefresh  = New-Ctl 'Button' $tStatus @{ Text = 'Refresh'; Location = (P 10 445); Size = (S 100 32) }
$bPrevWarn = New-Ctl 'Button' $tStatus @{ Text = 'Preview warning'; Location = (P 120 445); Size = (S 130 32) }
$bPrevCd   = New-Ctl 'Button' $tStatus @{ Text = 'Preview countdown'; Location = (P 260 445); Size = (S 140 32) }
$bLog      = New-Ctl 'Button' $tStatus @{ Text = 'Open log'; Location = (P 410 445); Size = (S 100 32) }
$bUninst   = New-Ctl 'Button' $tStatus @{ Text = 'Uninstall'; Location = (P 730 445); Size = (S 100 32); ForeColor = $Red }

function Get-StatusText {
    $sb = New-Object System.Text.StringBuilder
    $add = { param($l) [void]$sb.AppendLine($l) }
    $cfg = Get-NGConfig
    if (-not $cfg) {
        & $add 'NOT INSTALLED'
        & $add ''
        & $add 'Look through the settings tabs, then click "Install" at the bottom right.'
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
    $btnSave.Text = if ($installed) { 'Save and apply' } else { 'Install' }
    $bUninst.Enabled = $installed
    Update-TrustTab
}

# ---------------------------------------------------------------- Schedule tab
New-Ctl 'Label' $tSched @{ Location = (P 10 10); Size = (S 820 36); Text = "Each night is named after the evening it starts. A bedtime before 12:00 counts as after midnight, so Mon 00:30 means 12:30 AM early Tuesday. Untick a night to leave it free." } | Out-Null
$gridSched = New-Grid $tSched (P 10 52) (S 540 205) $false
Add-Col $gridSched 'TextBox'  'Night'   'Night'   70 $true | Out-Null
Add-Col $gridSched 'CheckBox' 'On'      'Locked'  60 | Out-Null
Add-Col $gridSched 'TextBox'  'Bedtime' 'Bedtime' 90 | Out-Null
Add-Col $gridSched 'TextBox'  'Unlock'  'Unlock'  90 | Out-Null
Add-Col $gridSched 'TextBox'  'Note'    ''        210 $true | Out-Null
foreach ($d in $NGDays) { [void]$gridSched.Rows.Add($d, $true, '00:00', '06:00', '') }

function Update-SchedNotes {
    for ($i = 0; $i -lt 7; $i++) {
        $r = $gridSched.Rows[$i]
        $bt = [string]$r.Cells['Bedtime'].Value
        $note = ''
        if ($bt -match $NGTimeRegex -and [timespan]::Parse($bt).TotalHours -lt 12) { $note = 'after midnight (' + $NGDays[($i + 1) % 7] + ')' }
        $r.Cells['Note'].Value = $note
    }
}
$gridSched.Add_CellEndEdit({ Update-SchedNotes })

$grpQuick = New-Ctl 'GroupBox' $tSched @{ Text = 'Quick set'; Location = (P 565 52); Size = (S 265 205) }
New-Ctl 'Label' $grpQuick @{ Text = 'Bedtime'; Location = (P 12 28); Size = (S 70 20) } | Out-Null
$txtQB = New-Ctl 'TextBox' $grpQuick @{ Text = '00:00'; Location = (P 90 25); Size = (S 70 24) }
New-Ctl 'Label' $grpQuick @{ Text = 'Unlock'; Location = (P 12 58); Size = (S 70 20) } | Out-Null
$txtQU = New-Ctl 'TextBox' $grpQuick @{ Text = '06:00'; Location = (P 90 55); Size = (S 70 24) }
$bQAll  = New-Ctl 'Button' $grpQuick @{ Text = 'Apply to all nights'; Location = (P 12 92); Size = (S 240 30) }
$bQWeek = New-Ctl 'Button' $grpQuick @{ Text = 'Apply to weeknights (Sun-Thu)'; Location = (P 12 128); Size = (S 240 30) }
$bQEnd  = New-Ctl 'Button' $grpQuick @{ Text = 'Apply to weekend (Fri, Sat)'; Location = (P 12 164); Size = (S 240 30) }

function Set-QuickNights([string[]]$Days) {
    $bt = Normalize-Time $txtQB.Text; $ut = Normalize-Time $txtQU.Text
    if ($bt -notmatch $NGTimeRegex -or $ut -notmatch $NGTimeRegex) { Msg 'Type times like 23:30.' 'Warning'; return }
    $gridSched.EndEdit() | Out-Null
    foreach ($r in $gridSched.Rows) {
        if ($Days -contains [string]$r.Cells['Night'].Value) { $r.Cells['On'].Value = $true; $r.Cells['Bedtime'].Value = $bt; $r.Cells['Unlock'].Value = $ut }
    }
    Update-SchedNotes
}
$bQAll.Add_Click({ Set-QuickNights $NGDays })
$bQWeek.Add_Click({ Set-QuickNights @('Sun', 'Mon', 'Tue', 'Wed', 'Thu') })
$bQEnd.Add_Click({ Set-QuickNights @('Fri', 'Sat') })

New-Ctl 'Label' $tSched @{ Location = (P 10 270); Size = (S 820 60); ForeColor = [System.Drawing.Color]::DimGray
    Text = "Safety limits: a night can be at most 16 hours, and there must be at least 6 free hours between nights, so the PC is always usable during the day." } | Out-Null

# ---------------------------------------------------------------- Warnings tab
New-Ctl 'Label' $tWarn @{ Location = (P 10 10); Size = (S 820 36); Text = "Up to 6 popups before bedtime. In the message you can use {minutes} {bedtime} {unlock} {action}, and \n for a new line. Add a row at the bottom; select a row and press Delete to remove it." } | Out-Null
$gridWarn = New-Grid $tWarn (P 10 52) (S 820 300) $true
Add-Col $gridWarn 'TextBox'  'Minutes' 'Minutes before' 110 | Out-Null
Add-Col $gridWarn 'CheckBox' 'Sound'   'Sound'          60 | Out-Null
$colMsg = Add-Col $gridWarn 'TextBox' 'Message' 'Message' 600
$colMsg.AutoSizeMode = 'Fill'
$gridWarn.Add_DefaultValuesNeeded({
    param($s, $e)
    $e.Row.Cells['Minutes'].Value = '15'
    $e.Row.Cells['Sound'].Value = $true
    $e.Row.Cells['Message'].Value = 'Bedtime in {minutes} minutes. The PC will {action} at {bedtime}.'
})
$bWPrev = New-Ctl 'Button' $tWarn @{ Text = 'Preview selected warning'; Location = (P 10 362); Size = (S 190 32) }

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
$lstApps = New-Ctl 'ListBox' $tBlock @{ Location = (P 280 66); Size = (S 420 120); HorizontalScrollbar = $true }
$bAddExe = New-Ctl 'Button' $tBlock @{ Text = 'Add program...'; Location = (P 710 66); Size = (S 120 28) }
$bAddDir = New-Ctl 'Button' $tBlock @{ Text = 'Add folder...'; Location = (P 710 100); Size = (S 120 28) }
$bDelApp = New-Ctl 'Button' $tBlock @{ Text = 'Remove'; Location = (P 710 134); Size = (S 120 28) }

New-Ctl 'Label' $tBlock @{ Text = 'Block at these times (add a row at the bottom; a slot like 22:00-02:00 crosses midnight):'; Location = (P 280 196); Size = (S 550 20) } | Out-Null
$gridWin = New-Grid $tBlock (P 280 218) (S 550 178) $true
foreach ($d in $NGDays) { Add-Col $gridWin 'CheckBox' $d $d 44 | Out-Null }
Add-Col $gridWin 'TextBox' 'From' 'From' 70 | Out-Null
Add-Col $gridWin 'TextBox' 'To' 'To' 70 | Out-Null
$gridWin.Add_DefaultValuesNeeded({
    param($s, $e)
    foreach ($d in $NGDays) { $e.Row.Cells[$d].Value = ($d -notin @('Sat', 'Sun')) }
    $e.Row.Cells['From'].Value = '09:00'; $e.Row.Cells['To'].Value = '17:00'
})
New-Ctl 'Label' $tBlock @{ Location = (P 280 402); Size = (S 550 90); ForeColor = [System.Drawing.Color]::DimGray
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

# ============================================================================
# First run: a few clicks to get going; everything else is on the tabs
# ============================================================================
function Show-FirstRun($Cfg) {
    $d = New-Ctl 'Form' $null @{ Text = 'Welcome to NightGuard'; Size = (S 480 400); StartPosition = 'CenterScreen'
        FormBorderStyle = 'FixedDialog'; MaximizeBox = $false; MinimizeBox = $false; Font = $FontUI }
    New-Ctl 'Label' $d @{ Location = (P 20 16); Size = (S 430 60); Font = (New-Object System.Drawing.Font('Segoe UI', 11))
        Text = "Let's get you to bed on time.`nPick your bedtime and you're done. Everything else can be changed later." } | Out-Null
    $first = $Cfg.Schedule.Mon
    New-Ctl 'Label' $d @{ Text = 'Bedtime (every night):'; Location = (P 20 96); Size = (S 200 20) } | Out-Null
    $tB = New-Ctl 'TextBox' $d @{ Text = $first.Bedtime; Location = (P 230 93); Size = (S 80 24); Font = $FontMono }
    New-Ctl 'Label' $d @{ Text = 'PC unlocks again at:'; Location = (P 20 130); Size = (S 200 20) } | Out-Null
    $tU = New-Ctl 'TextBox' $d @{ Text = $first.Unlock; Location = (P 230 127); Size = (S 80 24); Font = $FontMono }
    New-Ctl 'Label' $d @{ Text = 'At bedtime the PC will:'; Location = (P 20 164); Size = (S 200 20) } | Out-Null
    $cA = New-Ctl 'ComboBox' $d @{ Location = (P 230 161); Size = (S 140 24); DropDownStyle = 'DropDownList' }
    foreach ($a in $NGActions) { [void]$cA.Items.Add($a) }
    $cA.SelectedItem = [string]$Cfg.Action
    $cS = New-Ctl 'CheckBox' $d @{ Text = 'Also block social media sites at bedtime'; Location = (P 20 200); Size = (S 400 24); Checked = $true }
    New-Ctl 'Label' $d @{ Location = (P 20 232); Size = (S 430 40); ForeColor = [System.Drawing.Color]::DimGray
        Text = 'You get warnings 30 and 5 minutes before bedtime. Settings are locked for the last hour before bedtime.' } | Out-Null
    $ok = New-Ctl 'Button' $d @{ Text = 'Install'; Location = (P 20 300); Size = (S 140 34); Font = $FontBold; DialogResult = 'OK' }
    $more = New-Ctl 'Button' $d @{ Text = 'Customize first...'; Location = (P 170 300); Size = (S 150 34); DialogResult = 'Retry' }
    New-Ctl 'Button' $d @{ Text = 'Cancel'; Location = (P 330 300); Size = (S 110 34); DialogResult = 'Cancel' } | Out-Null
    $d.AcceptButton = $ok
    while ($true) {
        $r = $d.ShowDialog()
        if ($r -ne 'OK') { $d.Dispose(); return [string]$r }
        $bt = Normalize-Time $tB.Text; $ut = Normalize-Time $tU.Text
        foreach ($day in $NGDays) { $Cfg.Schedule.$day.Enabled = $true; $Cfg.Schedule.$day.Bedtime = $bt; $Cfg.Schedule.$day.Unlock = $ut }
        $Cfg.Action = [string]$cA.SelectedItem
        $Cfg.Blocking.Enabled = $cS.Checked
        $Cfg.Blocking.DuringBedtime = $true
        $errs = Test-NGConfig $Cfg
        if ($errs.Count -gt 0) {
            [void][System.Windows.Forms.MessageBox]::Show($d, ("Please fix these:`n`n- " + ($errs -join "`n- ")), 'NightGuard', 'OK', 'Warning')
            continue
        }
        try {
            $tmp = Join-Path $env:TEMP 'nightguard-settings.json'
            Set-Content -Path $tmp -Value (ConvertTo-NGJson $Cfg) -Encoding UTF8
            $d.Cursor = 'WaitCursor'
            & (Join-Path $PSScriptRoot 'install.ps1') -ConfigPath $tmp -NoPause -ThrowOnError | Out-Null
            Remove-Item $tmp -Force -ErrorAction SilentlyContinue
            [void][System.Windows.Forms.MessageBox]::Show($d, "NightGuard is on guard. Sweet dreams!`n`nOpen it any time from Start Menu > NightGuard.", 'NightGuard', 'OK', 'Information')
            $d.Dispose()
            return 'Installed'
        } catch {
            $d.Cursor = 'Default'
            [void][System.Windows.Forms.MessageBox]::Show($d, $_.Exception.Message, 'NightGuard', 'OK', 'Error')
        }
    }
}

# ============================================================================
# Moving settings between the form and the config
# ============================================================================
function Load-Form($Cfg) {
    for ($i = 0; $i -lt 7; $i++) {
        $s = $Cfg.Schedule.($NGDays[$i]); $r = $gridSched.Rows[$i]
        $r.Cells['On'].Value = [bool]$s.Enabled; $r.Cells['Bedtime'].Value = $s.Bedtime; $r.Cells['Unlock'].Value = $s.Unlock
    }
    Update-SchedNotes
    $gridWarn.Rows.Clear()
    foreach ($w in @($Cfg.Warnings | Where-Object { $_ })) { [void]$gridWarn.Rows.Add([string]$w.MinutesBefore, [bool]$w.Sound, [string]$w.Message) }
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
    $gridWin.Rows.Clear()
    foreach ($w in @($b.Windows | Where-Object { $_ })) {
        $vals = @()
        foreach ($d in $NGDays) { $vals += (@($w.Days) -contains $d) }
        $vals += [string]$w.From; $vals += [string]$w.To
        [void]$gridWin.Rows.Add($vals)
    }
}

function Read-Form {
    foreach ($g in @($gridSched, $gridWarn, $gridWin)) { [void]$g.EndEdit() }
    $sched = [ordered]@{}
    foreach ($r in $gridSched.Rows) {
        $sched[[string]$r.Cells['Night'].Value] = [pscustomobject]@{
            Enabled = [bool]$r.Cells['On'].Value
            Bedtime = Normalize-Time ([string]$r.Cells['Bedtime'].Value)
            Unlock  = Normalize-Time ([string]$r.Cells['Unlock'].Value)
        }
    }
    $warn = @()
    foreach ($r in $gridWarn.Rows) {
        if ($r.IsNewRow) { continue }
        $m = 0
        if (-not [int]::TryParse(([string]$r.Cells['Minutes'].Value).Trim(), [ref]$m)) { $m = -1 }
        $warn += [pscustomobject]@{ MinutesBefore = $m; Sound = [bool]$r.Cells['Sound'].Value; Message = [string]$r.Cells['Message'].Value }
    }
    $wins = @()
    foreach ($r in $gridWin.Rows) {
        if ($r.IsNewRow) { continue }
        $days = @($NGDays | Where-Object { [bool]$r.Cells[$_].Value })
        $wins += [pscustomobject]@{ Days = $days; From = Normalize-Time ([string]$r.Cells['From'].Value); To = Normalize-Time ([string]$r.Cells['To'].Value) }
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

function Invoke-Preview([string]$Script, [string[]]$ScriptArgs) {
    # Preview with the settings currently on screen
    $tmp = Join-Path $env:TEMP 'nightguard-preview'
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'common.ps1'), (Join-Path $PSScriptRoot $Script) -Destination $tmp -Force
    Set-Content -Path (Join-Path $tmp 'config.json') -Value (ConvertTo-NGJson (Read-Form)) -Encoding UTF8
    Start-Process powershell.exe -ArgumentList (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$(Join-Path $tmp $Script)`"") + $ScriptArgs)
}
$bPrevWarn.Add_Click({ Safe { Invoke-Preview 'warn.ps1' @('-Preview', '-Index', '1') } })
$bPrevCd.Add_Click({ Safe { Invoke-Preview 'session.ps1' @('-Preview') } })
$bWPrev.Add_Click({ Safe {
    $i = 1
    if ($gridWarn.CurrentRow -and -not $gridWarn.CurrentRow.IsNewRow) {
        # Warnings are sorted by minutes on save; find the selected one's position after sorting
        $sel = 0; [void][int]::TryParse([string]$gridWarn.CurrentRow.Cells['Minutes'].Value, [ref]$sel)
        $sorted = @((Read-Form).Warnings | ForEach-Object { [int]$_.MinutesBefore })
        $pos = [array]::IndexOf($sorted, $sel)
        if ($pos -ge 0) { $i = $pos + 1 }
    }
    Invoke-Preview 'warn.ps1' @('-Preview', '-Index', "$i")
} })
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
    if (-not (Get-NGConfig)) {
        $r = Show-FirstRun (Get-StartingConfig)
        if ($r -eq 'Cancel') { return }
    }
    Reload-Form
    if (-not (Get-NGConfig)) { $tabs.SelectedTab = $tSched }
    [void]$form.ShowDialog()
} catch {
    [void][System.Windows.Forms.MessageBox]::Show("NightGuard could not start: $($_.Exception.Message)`n`nYou can use TextMenu.cmd instead.", 'NightGuard', 'OK', 'Error')
} finally {
    $timer.Stop()
}
