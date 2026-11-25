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
- 🎮 **Daily game and screen time budgets** (optional). "1 hour of games on weekdays" actually means 1 hour. Then the games close. Rude, but fair.
- 🚫 **Blocks distractions on a schedule** (optional). Social media sites and apps like Discord or Steam, during bedtime or during your focus hours.
- 🔐 **Trusted-person mode** (optional). Give an 8-digit code to a friend or parent. Without it, you can't change settings or uninstall. No more "I'll just disable it tonight".
- 🌘 **Night mode.** In the last hour before bedtime, settings are locked. Past-you made a decision. Present-you doesn't get a vote.
- 🏁 **End date** (optional). Do a 30-day challenge and NightGuard removes itself afterwards.

---

## 🚀 Install (about one minute)

1. Click the green **Code** button above, then **Download ZIP**.
2. Unzip it anywhere, for example your Downloads folder.
3. Double-click **`NightGuard.cmd`** and click **Yes** on the admin prompt.
4. Click through the setup wizard. The defaults are sensible, so **Next, Next, Next** is fine. Check the **Review** step and click **Install NightGuard**. Done. Go to bed. (Well, at bedtime.)

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

NightGuard opens as a step-by-step wizard. Use **Next** and **Back**, or jump to any step in the list on the left. Nothing changes until you click **Save**. To change something later, open NightGuard again and go straight to that step.

Times are shown the same way as your Windows clock: 12-hour with AM/PM, or 24-hour. Click the hour or minute and use the arrows, or just type.

| Step | What you can set | Default |
|---|---|---|
| **Bedtimes** | Bedtime and unlock time for each night. Untick nights to leave them free. | Every night, 00:00 to 06:00 |
| **Warnings** | Up to 6 popups: how many minutes before, the message, sound on or off | 30 and 5 minutes before |
| **At bedtime** | Shutdown, Sleep, Hibernate, Lock or Sign out. Countdown length, how often it repeats, night mode length, end date. | Shutdown, 60 s countdown, repeats every 5 min |
| **Block sites and apps** | Websites, apps or whole folders to block, and when | Off (a social media list is ready to go) |
| **Trusted person** | Turn on the code lock | Off |
| **Game and screen time** | Daily game and screen time budgets (weekdays and weekends), warnings, what happens when time runs out, which programs count as games | Off (1 h games / 4 h screen on weekdays, 2 h / 6 h at weekends when you turn it on) |

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

## 🎮 Screen time limits

Turn them on in the **Game and screen time** step.

- **Game time** counts while a game is running and you're at the screen. It warns you at 15, 5 and 1 minutes left. When it's used up, games (and optionally launchers like Steam) are closed, and closed again if you reopen them.
- **Screen time** counts every minute you're signed in and the screen isn't locked. When it runs out, NightGuard **locks** the PC (default), **shuts it down**, or just **nags** you, your choice.
- **The day resets at 4 AM**, not midnight, so that 1 AM "quick game" counts toward the evening it belongs to. Nice try.
- **What counts as a game?** Anything running from a game folder (Steam, Epic, Xbox, GOG, Riot, Ubisoft, EA and Battle.net folders are pre-filled), plus any extra program names you add. Add your own library folders, such as `D:\SteamLibrary\steamapps\common`.
- Windows' own programs are never counted or closed, and screen time can't be set below 30 minutes. Heads-up: once today's screen time is used up with **Lock** or **Shutdown**, the PC stays locked or off until the day resets (4 AM by default). That's the deal you made with yourself. Safe Mode still works if you ever really need in.

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

- It only creates **its own scheduled tasks** (all in a `NightGuard` folder in Task Scheduler), **its own firewall rules** (group `NightGuard`), **one marked section in the hosts file**, its folder in `C:\ProgramData\NightGuard`, and Start Menu shortcuts. Nothing touches boot settings, drivers, accounts, or your network setup.
- Settings that could cause a shutdown loop are refused: a night can be at most 16 hours, and there must be at least 6 free hours between nights.
- There's always a countdown of at least 15 seconds before anything happens.
- **Damaged settings or a wrong clock?** NightGuard does nothing rather than risk a shutdown loop. If the settings file can't be read, or the clock is earlier than when the settings were saved (for example after a BIOS reset), it stands down and writes why in the log.
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

1. Open NightGuard and click **Preview warning** and **Preview countdown** on the Welcome step.
2. On the Bedtimes step, set *tonight's* night to a time a few minutes from now (for example bedtime 3:10 PM, unlocks 3:40 PM), set **Night mode** to `0` on the At bedtime step, and save.
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
| `tracker.ps1` | Counts game and screen time every minute |
| `notify.ps1` | Screen time popups |
| `common.ps1` | Shared code. The top of the file documents every setting. |

Settings are saved in `C:\ProgramData\NightGuard\config.json`, and there's a log in `log.txt` next to it.

**Upgrading from "Bedtime Lock"** (NightGuard's old name) or the separate **Screen Limits** tool? Just install NightGuard. It carries over your settings (and today's usage) and removes the old versions.

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
