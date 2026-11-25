<#
  NightGuard - uninstaller. Usually run from the app or the text menu.
  Refuses during night mode. In trusted-person mode it needs the code:
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
