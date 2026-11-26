<#
  NightGuard - uninstaller. Usually run from the app or the text menu.
  Refuses during night mode. Otherwise it needs either the trusted person's code,
  or (no end date set) an unlock request whose 7-day cool-off has passed:
    .\uninstall.ps1 -Code 12345678
  Removes only what NightGuard created: its scheduled tasks, its folder,
  its hosts-file section, its "NightGuard" firewall rules and its shortcuts.
#>
param(
    [string]$Code = '',
    [switch]$NoPause,
    [switch]$ThrowOnError
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

# The code is only ever digits; anything else must not reach the elevated command line
if ($Code -and $Code -notmatch '^\s*\d{1,12}\s*$') { Write-Host 'The code must be digits only.' -ForegroundColor Red; exit 1 }

$me = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $me.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Asking for administrator rights...'
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Code) { $argList += @('-Code', $Code) }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList
    exit
}

try {
    $cfg = Get-NGConfig
    $blocked = Get-NGBlockedReason $cfg
    if ($blocked) { throw "Nice try. $blocked" }
    Assert-NGCode $Code
    # Self-lock: without the trusted person's code, uninstall needs an open unlock (7-day cool-off) and no end date
    if (-not (Get-NGTrusted)) {
        $why = Get-NGUninstallBlock $cfg
        if ($why) { throw $why }
    }

    Remove-NightGuard
    try { & shutdown.exe /a 2>$null } catch { }   # cancel a pending shutdown, if any

    Write-Host ''
    Write-Host 'NightGuard removed.' -ForegroundColor Green
}
catch {
    if ($ThrowOnError) { throw }
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
}
if (-not $NoPause) { Write-Host ''; Read-Host 'Press Enter to close' | Out-Null }
