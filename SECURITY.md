# Security

NightGuard runs a Windows service as SYSTEM, listens on a local named pipe, and edits the hosts file and Windows Firewall, so security bugs matter.

## Reporting a vulnerability

Please **don't** open a public issue for security problems. Use GitHub's private reporting instead: go to the **Security** tab of this repository and click **Report a vulnerability**.

Useful things to include: what you found, how to reproduce it, and which Windows version you used.

## What counts

- A standard (non-admin) user getting admin or SYSTEM rights through NightGuard
- A standard user getting the service to do something the self-lock or trusted-person rules don't allow (through the named pipe or otherwise)
- NightGuard changing anything outside its own service, task, firewall group, hosts section or folders
- Getting around the trusted-person code without admin rights
- Getting around the self-lock (the 7-day cool-off before looser settings or uninstall) through the app or the service
- Anything that could leave a PC unusable outside bedtime hours

## What doesn't

- A Windows **administrator** removing NightGuard by hand.
- A Windows administrator editing NightGuard's files or changing the clock to skip the cool-off.
- Browsers with secure DNS skipping the hosts-file blocking (a known limit).
