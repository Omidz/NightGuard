# Contributing to NightGuard

Thanks for helping! Yes, even if you're doing this instead of something else you should be doing. We see you. 👀

## Ground rules

- **Safety first.** NightGuard must never brick a PC. Changes may only touch NightGuard's own service and watchdog task, its `NightGuard` firewall rule group, its marked section of the hosts file, `C:\Program Files\NightGuard`, `C:\ProgramData\NightGuard`, its own shortcuts, and (only with the user's yes) Windows' hibernate setting. No boot, driver, account or network-stack changes. When in doubt (bad settings, odd clock, Safe Mode), do nothing.
- **Keep the self-lock honest.** Looser settings and uninstall go through the 7-day cool-off (or the trusted code). The service checks every request itself; never move a rule into the app only.
- **No phoning home.** No telemetry, no network calls, no accounts.
- **Keep it lean.** C# on .NET 10, no third-party runtime dependencies. The service and tray app should stay light on CPU and memory.
- **Never weaken the trusted-person code.** No recovery or bypass paths in the tool itself.

## License and your contributions

NightGuard is source-available under the [PolyForm Strict License 1.0.0](LICENSE), which normally doesn't allow changes. As an extra permission, you may fork this repository and change it **only to send those changes back here as a pull request**.

By opening a pull request, you agree that Omid may use, change and publish your contribution as part of NightGuard, under any license, and that you have the right to give that permission.

## How to contribute

1. Open an issue first for anything bigger than a small fix, so we can agree on the approach.
2. Fork, create a branch, make your change.
3. Add or update tests in `tests/NightGuard.Core.Tests` for any rule you change (`dotnet test`).
4. Test on a real Windows PC (or a VM): install the MSI from your build, preview warnings, run a short test night, uninstall. Say what you tested in the pull request.
5. Open a pull request with a clear description. CI must be green (it builds the MSI too).

## Code layout

- `src/NightGuard.Core`: every rule (schedule, validation, self-lock, trusted code, streaks, screen time, hosts file) with no Windows dependencies. `Config/NightGuardConfig.cs` documents the settings format.
- `src/NightGuard.Service`: the Windows service. `Engine.cs` is the tick loop, `Requests.cs` handles what the app asks for.
- `src/NightGuard.App`: the WPF app (settings window and tray app).
- `installer/Product.wxs`: the WiX installer.
- `legacy/`: the original PowerShell version, for reference only.
