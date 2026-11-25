# 🌙 NightGuard

**A bedtime bouncer for your Windows PC.** For people who say "just one more match" at 11:58 PM and somehow see the sunrise.

NightGuard warns you before bedtime, then **powers your PC off** (hibernate by default, or shut down, sleep, lock, your call). If you turn it back on at 2 AM, it goes off again a minute later. And again. It's very patient. Way more patient than you.

> Built for **heavy** procrastinators. If you've ever googled "how to stop gaming at night" *while gaming at night*, hi, welcome, you're among friends.

---

## ✨ What it does

- ⏰ **Warns you** before bedtime (30 and 5 minutes by default). Popups, sound, custom messages, the works.
- 🔌 **Enforces bedtime.** Shutdown, sleep, hibernate, lock or sign out, with a short countdown so you can save.
- 🔁 **Doesn't fall for tricks.** Turn the PC back on during the night? It checks every minute and at every startup.
- 🔒 **No easy take-backs.** Once installed, you can make it stricter anytime, but loosening it or uninstalling needs a **7-day cool-off**. Like quitting anything else, the decision gets made with a clear head, not at 1 AM.
- 📅 **Different bedtime per night.** Strict on school or work nights, chill on Friday.
- 🎮 **Daily game and screen time budgets** (optional). "1 hour of games on weekdays" actually means 1 hour. Then the games close. Rude, but fair.
- 🚫 **Blocks distractions on a schedule** (optional). Social media sites and apps like Discord or Steam, during bedtime or during your focus hours.
- 🔐 **Trusted-person mode** (optional). A friend or parent types in their own secret 8-digit code. Without it, you can't change settings or uninstall. No more "I'll just disable it tonight".
- 🌘 **Night mode.** In the last hour before bedtime, settings are locked. Past-you made a decision. Present-you doesn't get a vote.
- 🏁 **End date** (optional). Do a 30-day challenge and NightGuard removes itself afterwards.

---

## 🚀 Quick start (about two minutes)

1. **Download** [**NightGuard.zip**](https://github.com/Omidz/NightGuard/releases/latest/download/NightGuard.zip) from the [latest release](https://github.com/Omidz/NightGuard/releases/latest). Just that one file. (Link not working? Use the green **Code** button > **Download ZIP** instead, same thing.)
2. **Unblock it** so Windows doesn't nag: right-click the zip > **Properties** > tick **Unblock** at the bottom > **OK**. (No Unblock box? Fine, skip it.)
3. **Unzip** it anywhere, for example your Downloads folder.
4. **Double-click `NightGuard.cmd`** and click **Yes** on the admin prompt. (Blue "Windows protected your PC" screen? Click **More info** > **Run anyway**. More fixes in the SmartScreen section just below.)
5. **Walk through the wizard.** Pick your bedtimes, warnings and what happens at bedtime. The defaults are sensible, so **Next, Next, Next** is totally fine.
6. **Install.** On the last step, read the summary, tick **Yes, these settings are correct**, and click **Install NightGuard**. Done!

Later, open NightGuard from **Start Menu > NightGuard**. You can delete the downloaded folder: NightGuard keeps its own copy.

Needs Windows 10 or 11. Nothing else to install: it uses the PowerShell that's already on every Windows PC.

### 🌙 What happens tonight

- **30 and 5 minutes before bedtime:** a popup (with a sound) tells you to wrap up and save.
- **At bedtime:** a 60-second countdown, then the PC hibernates. Your open stuff is saved to disk, usually.
- **Switch it back on before unlock time?** About a minute later: countdown, hibernate. Repeat until you give up and sleep. 😴
- **After unlock time:** your PC is back to normal, and your apps are where you left them.

### 🧪 Not sure yet? Test drive it first

- **During the day, no install needed:** on the Warnings step, click **Preview** on a popup and **Preview the bedtime countdown**. That's exactly what you'll see at night.
- **One-night trial:** on the At bedtime step, tick **Stop after an end date** and pick *today*. NightGuard guards tonight only, then removes itself in the morning. Liked it? Install it for real.

Because of the [7-day cool-off](#-no-easy-take-backs-the-self-lock), choose your settings with care. Stricter is always easy, looser takes a week.

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

NightGuard opens as a step-by-step wizard. Use **Next** and **Back**, or jump to any step in the list on the left. Nothing is installed or changed until the last step, **Review and install**: read the summary, tick **Yes, these settings are correct**, and the **Install NightGuard** button appears (it says **Save and apply** once NightGuard is installed). To change something later, open NightGuard again and go straight to that step. Stricter changes save right away; looser ones need the [7-day cool-off](#-no-easy-take-backs-the-self-lock).

Sections you switch off (like screen time or blocking) are greyed out, so you can see at a glance what's on. **Status and log** and **Request uninstall** live at the bottom left, out of the wizard's way.

Times are shown the same way as your Windows clock: 12-hour with AM/PM, or 24-hour. Each time box is labelled with the format it uses. Click the hour or minute and use the arrows, or just type.

| Step | What you can set | Default |
|---|---|---|
| **Bedtimes** | Bedtime and unlock time for each night. Untick nights to leave them free. Use the **Set several nights at once** box to give every night, school nights or weekend nights the same times in one click. Each row shows which hours get locked. | Every night, 00:00 to 06:00 |
| **Warnings** | Up to 6 popups: how many minutes before, the message, sound on or off. **Preview** any popup, or the bedtime countdown, right there. | 30 and 5 minutes before |
| **At bedtime** | Hibernate, Shutdown, Sleep, Lock or Sign out. Countdown length, how often it repeats, night mode length, end date. | Hibernate, 60 s countdown, repeats every minute |
| **Game and screen time** | Daily game and screen time budgets (weekdays and weekends), warnings, what happens when time runs out, which programs count as games | Off (1 h games / 4 h screen on weekdays, 2 h / 6 h at weekends when you turn it on) |
| **Block sites and apps** | Websites, apps or whole folders to block, and when | Off (a social media list is ready to go) |
| **Trusted person** | Turn on the code lock. Your trusted person types their own code when you install or save. | Off |

**How nights are named:** after the evening they start. A bedtime before 12:00 counts as *after midnight*, so "Mon 00:30" means 12:30 AM, early Tuesday.

### ✍️ Writing popups that actually work on you

These fill themselves in: `{minutes}` (minutes left), `{bedtime}`, `{unlock}` and `{action}` (like "hibernate"). Type `\n` for a new line.

- Keep it short: one line of news, one line of what to do.
- Start kind, get firmer. Future you responds better to a plan than a lecture.
- 30 minutes before: `Heads up! Bedtime in {minutes} minutes.\nWrap up this round.`
- 5 minutes before: `FINAL WARNING!\nThe PC will {action} at {bedtime}. Save NOW.`

Click **Fill in example messages** on the Warnings step to start from these, and **Preview** to see exactly how each popup looks.

### Which bedtime action?

| Action | Strength | The vibe |
|---|---|---|
| Hibernate (default, recommended) | 💪💪 | Saves your open stuff to disk, then powers off. Switch on after unlock time and it's all still there. |
| Shutdown | 💪💪💪 | Closes everything. Unsaved work is gone. Very effective. |
| Sleep | 💪 | Quick to resume, but it'll keep putting itself back to sleep. |
| Sign out | 💪 | Closes your apps and signs you out, again and again. |
| Lock | 🪶 | Just locks the screen. Fine if you only need a nudge. |

**About Hibernate.** It's the default because it's strong *and* kind: the PC really powers off, but your open apps come back when you switch it on again (and if you switch it on during locked hours, it hibernates again after the countdown). Two honest caveats:

- **It usually keeps your unsaved work, but it can't guarantee it.** A game that's online may disconnect, a Windows update or a power cut can still lose things. Save at the warnings anyway.
- **Hibernate has to be switched on in Windows.** It's off on many desktop PCs. When you install with Hibernate picked, NightGuard checks and, **if you say yes**, turns it on for you (`powercfg /hibernate on`). Windows then sets aside some disk space for it (a file about half the size of your RAM). If you say no, or your PC can't hibernate, NightGuard **shuts down instead**. Uninstalling NightGuard leaves the Windows hibernate setting as it is.

---

## 🎮 Screen time limits

Turn them on in the **Game and screen time** step.

- **Game time** counts while a game is running and you're at the screen. It warns you at 15, 5 and 1 minutes left. When it's used up, games (and optionally launchers like Steam) are closed, and closed again if you reopen them.
- **Screen time** counts every minute you're signed in and the screen isn't locked. When it runs out, NightGuard **locks** the PC (default), **shuts it down**, or just **nags** you, your choice.
- **The day resets at 4 AM**, not midnight, so that 1 AM "quick game" counts toward the evening it belongs to. Nice try.
- **What counts as a game?** Anything running from a game folder (Steam, Epic, Xbox, GOG, Riot, Ubisoft, EA and Battle.net folders are pre-filled), plus any extra program names you add. Add your own library folders, such as `D:\SteamLibrary\steamapps\common`.
- Windows' own programs are never counted or closed, and screen time can't be set below 30 minutes. Heads-up: once today's screen time is used up with **Lock** or **Shutdown**, the PC stays locked or off until the day resets (4 AM by default). That's the deal you made with yourself. Safe Mode still works if you ever really need in.

## 🔒 No easy take-backs (the self-lock)

NightGuard treats itself like a commitment, because "I'll just turn it off tonight" is exactly the problem.

- **Opening NightGuard again** (from the Start Menu or the downloaded folder) shows that it's already installed, when its last night is, and what you can change. It never offers a quick uninstall.
- **Stricter changes save right away:** earlier bedtime, more nights, more blocked sites, less game time, a stronger bedtime action.
- **Looser changes need an unlock:** later bedtime, fewer nights, removing blocks, more game time, a weaker action, a longer countdown. NightGuard lists what's looser and offers to **request an unlock**.
- **The unlock has a 7-day cool-off.** After it, you have 3 days to make looser changes or uninstall, once. Then it locks again. You can cancel a request anytime.
- **Uninstall** works the same way: **Request uninstall** (bottom left) starts the cool-off. When it's over, you get one last **ARE YOU SURE?**
- **Set an end date?** Then there's no uninstall before it, and the date can't be moved earlier. NightGuard removes itself after the last night, as promised.
- **Trusted-person mode on?** Your trusted person's code skips the cool-off (for changes and uninstall).
- **Night mode** still applies: during the last hour before bedtime and through the night, nothing can be changed at all.

Honest note: this is a speed bump for your 1 AM brain, not a prison. A Windows administrator can still [remove NightGuard by hand](#-removing-it-by-hand-last-resort), and could even fiddle with the clock. If you want it airtight, use a standard (non-admin) Windows account day to day and let someone you trust keep the admin password.

## 🔐 Trusted-person mode

Someone you trust (a parent, partner, friend, or the roommate who's tired of hearing you game at 3 AM) picks their own 8-digit code. From then on, **changing any setting or uninstalling needs the code.**

How it works:

1. Tick **Use a trusted-person code** on the Trusted person step.
2. On the last step, NightGuard double-checks you really understand the risk before the Install button appears.
3. Right after you click **Install** (or **Save**), hand the keyboard to your trusted person. They type their code twice, hidden. **NightGuard never shows, emails or prints it**, so you never see it. That's the whole point.

Too-easy codes like `12345678`, `00000000`, `12121212` or a birthday are refused.

> ### ⚠️ READ THIS. SERIOUSLY.
>
> **If the code is forgotten, there is NO way to recover it.** Not you, not your trusted person, not us. NightGuard only keeps a scrambled copy (a salted hash), so it can't tell anyone the code.
>
> - **Your trusted person has to remember it or store it safely** (a password manager is perfect). Not on a sticky note next to your PC.
> - **Don't ask them to tell you.** You'll just type it in at 1 AM. (We know you.)
> - Wrong guesses lock the code box for 15 minutes after 5 tries, and longer after each further try.
>
> **They GOT to have the code.**

Things that still work without the code, so you're never truly stuck:

- The PC always works normally **outside bedtime hours**.
- If you set an **end date**, NightGuard still removes itself after it.
- Without the code, you can still request an unlock and wait out the **7-day cool-off** (unless there's an end date).
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

The normal way is **Request uninstall** at the bottom left of the app (7-day cool-off), or the trusted person's code. If you can't use those (lost code, something broken, a real emergency) and you're a Windows admin, open **PowerShell as administrator** and run:

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

NightGuard is young. It's built carefully, but it hasn't been battle-tested on lots of PCs yet. Do the [test drive](#-not-sure-yet-test-drive-it-first) first: previews during the day, then a one-night trial with the end date set to today. It removes itself in the morning, and you install it for real with your settings dialled in.

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

**I installed it and now I regret it. Help?**
Request an unlock (or uninstall) in the app and wait out the 7-day cool-off. If you still want out after a week of decent sleep, fair enough: you'll get the button. We're betting you won't. 😉

**Can I cancel a shutdown "just this once"?**
That's exactly the kind of thing NightGuard is here to stop. During the day you can make it stricter anytime; looser takes the 7-day cool-off.

**Does it spy on me or phone home?**
No. No internet, no accounts, no tracking. It's a few PowerShell scripts you can read.

**I work night shifts.**
Set your own bedtime and unlock time per night. Any time works, as long as there are at least 6 free hours between nights.

**Will it close my game without saving?**
With Shutdown, yes, after the countdown. With Hibernate (the default) your game is usually still there when you switch the PC back on, but don't bet your save file on it. That's why there are warnings. Save at the 5-minute warning. 🙂

**Will it slow down my PC or my games?**
Nope. NightGuard isn't a program that sits in the background. Windows wakes a tiny script now and then (every minute for screen time or at night, every 5 minutes for blocking), it does its thing in a second or two, and it's gone again. No always-on background app, no meaningful memory use. Your FPS won't notice. Your sleep schedule will.

**Why does it repeat every minute at night? Isn't that a lot?**
Each check is a blink, and only during locked hours. Every minute means that if you switch the PC back on at 2 AM, you get about a minute before it goes back to bed. You can make it less often on the At bedtime step, but where's the fun in that.

---

## 🤝 Contributing

Ideas, bug fixes and translations are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md). Found a security issue? See [SECURITY.md](SECURITY.md).

## 📄 License

[MIT](LICENSE). Free to use, change and share.

Now close this tab and go to sleep. 🌙
