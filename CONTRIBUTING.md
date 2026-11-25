# Contributing to NightGuard

Thanks for helping! Yes, even if you're doing this instead of something else you should be doing. We see you. 👀

## Ground rules

- **Safety first.** NightGuard must never brick a PC. Changes may only touch NightGuard's own scheduled tasks, its `NightGuard` firewall rule group, its marked section of the hosts file, and `C:\ProgramData\NightGuard`. No boot, driver, account or network-stack changes.
- **No phoning home.** No telemetry, no network calls, no accounts.
- **Keep it simple.** Plain PowerShell 5.1 (built into Windows 10/11), no extra installs.
- **Never weaken the trusted-person code.** No recovery or bypass paths in the tool itself.

## How to contribute

1. Open an issue first for anything bigger than a small fix, so we can agree on the approach.
2. Fork, create a branch, make your change.
3. Test on a real Windows PC (or a VM): install, preview warnings, run a short test night, uninstall. Say what you tested in the pull request.
4. Keep `.ps1` and `.cmd` files with Windows (CRLF) line endings and ASCII text.
5. Open a pull request with a clear description.

## Code layout

`common.ps1` holds the shared logic and documents the settings format at the top. The other scripts are small and each do one job (see the README's file table).
