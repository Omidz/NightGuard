# 🌙 NightGuard

**A bedtime bouncer for your Windows PC.** For people who say "just one more match" at 11:58 PM and somehow see the sunrise.

NightGuard warns you before bedtime, then **shuts your PC down** (or puts it to sleep, or locks it, your call). If you turn it back on at 2 AM, it shuts down again. And again. It's very patient. Way more patient than you.

> Built for **heavy** procrastinators. If you've ever googled "how to stop gaming at night" *while gaming at night*, hi, welcome, you're among friends.

---

## ✨ What it does

- ⏰ **Warns you** before bedtime (30 and 5 minutes by default). Popups, sound, custom messages, the works.
- 🔌 **Enforces bedtime.** Shutdown, sleep, hibernate, lock or sign out, with a short countdown so you can save.
- 🔁 **Doesn't fall for tricks.** Turn the PC back on during the night? It checks every few minutes and at every startup.
- 📅 **Different bedtime per night.** Strict on school or work nights, chill on Friday.
- 🚫 **Blocks distractions on a schedule** (optional). Social media sites and apps like Discord or Steam, during bedtime or during your focus hours.
- 🔐 **Trusted-person mode** (optional). Give an 8-digit code to a friend or parent. Without it, you can't change settings or uninstall. No more "I'll just disable it tonight".
- 🌘 **Night mode.** In the last hour before bedtime, settings are locked. Past-you made a decision. Present-you doesn't get a vote.
- 🏁 **End date** (optional). Do a 30-day challenge and NightGuard removes itself afterwards.

---

## 🚀 Install (about one minute)

1. Click the green **Code** button above, then **Download ZIP**.
2. Unzip it anywhere, for example your Downloads folder.
3. Double-click **`NightGuard.cmd`** and click **Yes** on the admin prompt.
4. Pick your bedtime, click **Install**. Done. Go to bed. (Well, at bedtime.)

After that, open NightGuard from **Start Menu > NightGuard**.

Needs Windows 10 or 11. Nothing else to install: it uses the PowerShell that's already on every Windows PC.

### 🛡️ "Windows protected your PC" / SmartScreen blocked it?

Totally normal. Windows is suspicious of anything downloaded from the internet that isn't from a big company. Pick **one** of these fixes:

**Fix 1: Click past it.** On the blue screen, click **More info**, then **Run anyway**.

**Fix 2: Unblock the zip *before* unzipping.** Right-click the downloaded `.zip` > **Properties** > tick **Unblock** at the bottom > **OK**. Then unzip it again (replace the old folder). Now `NightGuard.cmd` opens normally.

**Fix 3: Paste one command (works when the others don't).**

1. Open the unzipped folder in File Explorer.
2. Right-click an empty spot > **Open in Terminal**. (Windows 10: **File > Open Windows PowerShell**.)
3. Paste this and press Enter:

   ```powershell
   Get-ChildItem | Unblock-File; powershell -NoProfile -ExecutionPolicy Bypass -File .\gui.ps1
   ```

4. Click **Yes** on the admin prompt.

Once it's installed, the Start Menu shortcut won't be blocked again.

---

## 🎛️ Settings

All of this is in the NightGuard window. Nothing changes until you click **Save and apply**.

| Tab | What you can set | Default |
|---|---|---|
| **Schedule** | Bedtime and unlock time for each night. Untick nights to leave them free. | Every night, 00:00 to 06:00 |
| **Warnings** | Up to 6 popups: how many minutes before, the message, sound on or off | 30 and 5 minutes before |
| **Bedtime action** | Shutdown, Sleep, Hibernate, Lock or Sign out. Countdown length, how often it repeats, night mode length, end date. | Shutdown, 60 s countdown, repeats every 5 min |
| **Block sites and apps** | Websites, apps or whole folders to block, and when | Off (a social media list is ready to go) |
| **Trusted person** | Turn on the code lock | Off |
| **Screen time** | Shows your daily game and screen time, if the Screen Limits add-on is installed | — |

**How nights are named:** after the evening they start. A bedtime before 12:00 counts as *after midnight*, so "Mon 00:30" means 12:30 AM, early Tuesday.

**Warning messages** can use `{minutes}`, `{bedtime}`, `{unlock}` and `{action}`, which fill in automatically. Use `\n` for a new line.

### Which bedtime action?

| Action | Strength | The vibe |
|---|---|---|
| Shutdown | 💪💪💪 | Closes everything. Unsaved work is gone. Very effective. |
| Hibernate | 💪💪 | Saves your open stuff to disk, then powers off. |
| Sleep | 💪 | Quick to resume, but it'll keep putting itself back to sleep. |
| Sign out | 💪 | Closes your apps and signs you out, again and again. |
| Lock | 🪶 | Just locks the screen. Fine if you only need a nudge. |

---

## 🔐 Trusted-person mode

You give an 8-digit code to someone you trust. From then on, **changing any setting or uninstalling needs the code.** NightGuard shows the code once, then helps you email, copy or print it.

> ### ⚠️ READ THIS. SERIOUSLY.
>
> **If the code is lost, there is NO way to recover it.** Not you, not your trusted person, not us. NightGuard only keeps a scrambled copy (a salted hash), so it can't tell you the code.
>
> - **Give the code to your trusted person before you turn it on.** Print it, email it, write it on paper. Keep it somewhere safe.
> - **Don't keep the only copy yourself**, or you'll just type it in at 1 AM. (We know you.)
> - Wrong guesses lock the code box for 15 minutes after 5 tries, and longer after each further try.
>
> **You GOT to have the code.**

Things that still work without the code, so you're never truly stuck:

- The PC always works normally **outside bedtime hours**.
- If you set an **end date**, NightGuard still removes itself after it.
- A Windows **administrator can remove NightGuard by hand** (see [Removing it by hand](#-removing-it-by-hand-last-resort)). That's on purpose: it's a tool to help you, not malware. For a real lock, use a standard (non-admin) Windows account day to day, and let your trusted person keep the admin password too.

---

## 🚫 Blocking sites and apps

- **Websites** are blocked through a clearly marked section of the Windows `hosts` file.
- **Apps** are blocked with Windows Firewall rules in a group called `NightGuard`. This cuts the app's internet; it doesn't close the app. Add a whole folder (like your Steam or Discord folder) to block everything inside it.
- Blocks switch on and off on schedule (checked every 5 minutes) and are **removed automatically** when the time slot ends, on uninstall, or if anything goes wrong.
- Windows-critical sites (Microsoft, Windows Update, sign-in, security certificates) and Windows' own folders can't be blocked, so you can't break your PC by accident.

**Browser loophole:** browsers with "secure DNS" (DNS over HTTPS) turned on can skip the hosts file. Turn it off to make site blocking stick:

- **Chrome:** Settings > Privacy and security > Security > *Use secure DNS* > off
- **Edge:** Settings > Privacy, search, and services > Security > *Use secure DNS* > off
- **Firefox:** Settings > Privacy & Security > *DNS over HTTPS* > Off

---

## 🧯 Safety: it won't brick your PC

NightGuard was built to be strict at night and harmless otherwise:

- It only creates **its own scheduled tasks**, **its own firewall rules** (group `NightGuard`), **one marked section in the hosts file**, its folder in `C:\ProgramData\NightGuard`, and Start Menu shortcuts. Nothing touches boot settings, drivers, accounts, or your network setup.
- Settings that could cause a shutdown loop are refused: a night can be at most 16 hours, and there must be at least 6 free hours between nights.
- There's always a countdown of at least 15 seconds before anything happens.
- **Safe Mode is always safe:** NightGuard never acts in Safe Mode.
- If blocking hits an error, it removes the blocks rather than leaving things half-blocked.

### 🔧 Removing it by hand (last resort)

The normal way is the **Uninstall** button. If you can't use it (lost code, something broken) and you're a Windows admin, open **PowerShell as administrator** and run:

```powershell
Get-ScheduledTask -TaskPath '\NightGuard\' | Unregister-ScheduledTask -Confirm:$false
Get-NetFirewallRule -Group 'NightGuard' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$h = "$env:SystemRoot\System32\drivers\etc\hosts"
(Get-Content $h -Raw) -replace '(?ms)^# >>> NightGuard BEGIN.*?^# <<< NightGuard END[^\n]*\n?', '' | Set-Content $h -NoNewline
Remove-Item "$env:ProgramData\NightGuard" -Recurse -Force
Remove-Item "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\NightGuard*.lnk"
```

If it's during locked hours, boot into **Safe Mode** first (hold **Shift** while clicking **Restart** > Troubleshoot > Advanced options > Startup Settings > Restart > **4**) and run it there.

---

## 🧪 Try it before you trust it

NightGuard is young. It's built carefully, but it hasn't been battle-tested on lots of PCs yet. Do a quick daytime test:

1. Open NightGuard and click **Preview warning** and **Preview countdown** on the Status tab.
2. On the Schedule tab, set *tonight's* night to a time a few minutes from now (for example bedtime `15:10`, unlock `15:40`), set **Night mode** to `0` on the Bedtime action tab, and save.
3. Watch the warnings, then the shutdown. Turn the PC on again and check it shuts down again.
4. After the unlock time, set your real schedule.

Something weird? [Open an issue](../../issues). Bug reports from fellow procrastinators are very welcome (ideally filed before midnight 😉).

---

## 🗂️ What's in the box

| File | What it does |
|---|---|
| `NightGuard.cmd` | Double-click to open NightGuard |
| `TextMenu.cmd` | A text-only menu, in case the window ever misbehaves |
| `gui.ps1` | The NightGuard window |
| `menu.ps1` | The text menu |
| `install.ps1` / `uninstall.ps1` | Save settings and set up tasks / remove everything |
| `enforce.ps1` | Runs at night as SYSTEM: shutdown, sleep or hibernate |
| `session.ps1` | Shows the countdown, and locks or signs out |
| `warn.ps1` | The warning popups |
| `blocker.ps1` | Turns site and app blocking on and off |
| `common.ps1` | Shared code. The top of the file documents every setting. |

Settings are saved in `C:\ProgramData\NightGuard\config.json`, and there's a log in `log.txt` next to it.

**Upgrading from "Bedtime Lock"** (NightGuard's old name)? Just install NightGuard. It carries over your settings and removes the old version.

---

## 🙋 FAQ

**Can I cancel a shutdown "just this once"?**
That's exactly the kind of thing NightGuard is here to stop. You *can* change settings during the day, outside night mode.

**Does it spy on me or phone home?**
No. No internet, no accounts, no tracking. It's a few PowerShell scripts you can read.

**I work night shifts.**
Set your own bedtime and unlock time per night. Any time works, as long as there are at least 6 free hours between nights.

**Will it close my game without saving?**
With Shutdown, yes, after the countdown. That's why there are warnings. Save at the 5-minute warning. 🙂

---

## 🤝 Contributing

Ideas, bug fixes and translations are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md). Found a security issue? See [SECURITY.md](SECURITY.md).

## 📄 License

[MIT](LICENSE). Free to use, change and share.

Now close this tab and go to sleep. 🌙
