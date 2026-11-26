# 🌙 NightGuard

**A bedtime bouncer for your Windows PC.** For people who say "just one more match" at 11:58 PM and somehow see the sunrise.

NightGuard warns you before bedtime, then **powers your PC off** (hibernate by default, or shut down, sleep, lock, your call). If you turn it back on at 2 AM, it goes off again a minute later. And again. It's very patient. Way more patient than you.

> Built for **heavy** procrastinators. If you've ever googled "how to stop gaming at night" *while gaming at night*, hi, welcome, you're among friends.

---

## ✨ What it does

- ⏰ **Warns you** before bedtime (30 and 5 minutes by default). Popups, sound, custom messages, the works.
- 🔌 **Enforces bedtime.** Shutdown, sleep, hibernate, lock or sign out, with a short countdown so you can save.
- 🔁 **Doesn't fall for tricks.** Turn the PC back on during the night? It checks every minute, at every startup and every time the PC wakes up. Cancel the shutdown with `shutdown /a`? It shuts down anyway. Close the countdown window? It locks anyway. "Move" to Hawaii in the time zone settings at 11:59 PM? Nice try: NightGuard keeps its own clock.
- 🔒 **No easy take-backs.** Once installed, you can make it stricter anytime, but loosening it or uninstalling needs a **7-day cool-off**. Like quitting anything else, the decision gets made with a clear head, not at 1 AM.
- 📅 **Different bedtime per night.** Strict on school or work nights, chill on Friday.
- 🎮 **Daily game and screen time budgets** (optional). "1 hour of games on weekdays" actually means 1 hour. Then the games close. Rude, but fair.
- 🚫 **Blocks distractions on a schedule** (optional). Social media sites and apps like Discord or Steam, during bedtime or during your focus hours.
- 🔐 **Trusted-person mode** (optional). A friend or parent types in their own secret 8-digit code. Without it, you can't change settings or uninstall. No more "I'll just disable it tonight".
- 🌘 **Night mode.** In the last hour before bedtime, settings are locked. Past-you made a decision. Present-you doesn't get a vote.
- ☀️ **Morning check-in.** A little cheer for last night, your streak of clean nights, **badges from night 1 to a full year**, and an optional mood + note for your private journal. Recovery is a streak game.
- 💬 **Your why.** Write down why you started. NightGuard throws it back at you in the final warning, right when you need it.
- 🎯 **Gets through to you mid-game.** Warnings and the countdown force themselves on top, with a sound and a Windows notification as backup.
- 🏁 **End date** (optional). Do a 30-day challenge and NightGuard removes itself afterwards.

---

## 🚀 Quick start (about two minutes)

1. **Download** [**NightGuard.msi**](https://github.com/Omidz/NightGuard/releases/latest/download/NightGuard.msi) from the [latest release](https://github.com/Omidz/NightGuard/releases/latest).
2. **Double-click it** and follow the installer (accept the license, click **Install**, then **Yes** on the admin prompt). Blue "Windows protected your PC" screen? Click **More info** > **Run anyway** (more in the SmartScreen section just below).
3. **NightGuard opens by itself** when the installer finishes. If it doesn't, open it from the **Start Menu**.
4. **Walk through the setup.** Pick your bedtimes, warnings and what happens at bedtime. The defaults are sensible, so **Next, Next, Next** is totally fine. One thing we *strongly* recommend: write your **why** on the "Your why and journal" step.
5. **Start it.** On the last step, read the summary, tick **Yes, these settings are correct**, and click **Start NightGuard**. Done!

**What got installed:** the NightGuard app in `C:\Program Files\NightGuard`, and a small **Windows service** that does the guarding. The service starts with Windows, before anyone signs in, and restarts itself if anything stops it. Closing the NightGuard window (or the tray icon) doesn't stop anything; that's on purpose. Want it on the taskbar? Right-click NightGuard in the Start Menu > **Pin to taskbar**.

Needs 64-bit Windows 10 or 11. Nothing else to install: the installer brings everything it needs.

**Already using the PowerShell version?** Just install the MSI. Your settings, trusted-person code, unlock request, streak and journal all carry over, and the old scheduled tasks and scripts are removed.

### 🌙 What happens tonight

- **30 and 5 minutes before bedtime:** a popup (with a sound) jumps in front of whatever you're doing and tells you to wrap up and save. The last one reminds you why you started.
- **At bedtime:** a full-screen 60-second countdown (even over your game; shrink it to save first), then the PC hibernates. Your open stuff is saved to disk, usually.
- **Switch it back on before unlock time?** About a minute later: countdown, hibernate. Repeat until you give up and sleep. 😴
- **After unlock time:** your PC is back to normal, and your apps are where you left them. Waking it from last night's hibernate won't set off a leftover countdown: anything NightGuard started before the PC went to sleep is cancelled on wake-up, and it double-checks the clock before it ever acts.
- **Your first sign-in of the day:** a morning check-in pops up to congratulate you (or gently roast you), shows your streak, and lets you log your mood. Or just click **Skip**.

### 🧪 Not sure yet? Test drive it first

- **During the day, nothing to commit to:** on the Warnings step, click **Preview** on a popup and **Preview the bedtime countdown**. That's exactly what you'll see at night.
- **One-night trial:** on the At bedtime step, tick **Stop after this last locked night** and pick *today*. NightGuard guards tonight only, then removes itself in the morning. Liked it? Install it again for real.

Because of the [7-day cool-off](#-no-easy-take-backs-the-self-lock), choose your settings with care. Stricter is always easy, looser takes a week.

### 🛡️ "Windows protected your PC" / SmartScreen blocked it?

Totally normal. The installer isn't code-signed yet, and Windows is suspicious of anything downloaded from the internet that isn't from a big company. Pick **one** of these fixes:

**Fix 1: Click past it.** On the blue screen, click **More info**, then **Run anyway**.

**Fix 2: Unblock it first.** Right-click the downloaded `NightGuard.msi` > **Properties** > tick **Unblock** at the bottom > **OK**. Then double-click it again.

Once it's installed, Windows won't ask again.

---

## 🎛️ Settings

NightGuard opens as a step-by-step wizard. Use **Next** and **Back**, or jump to any step in the list on the left. Nothing changes until the last step, **Review and save**: read the summary, tick **Yes, these settings are correct**, and the **Start NightGuard** button appears (it says **Save and apply** once NightGuard is running). To change something later, open NightGuard again (Start Menu, or double-click the moon in the tray) and go straight to that step. Stricter changes save right away; looser ones need the [7-day cool-off](#-no-easy-take-backs-the-self-lock). The review page lists anything that counts as looser before you save.

Sections you switch off (like screen time or blocking) are greyed out, so you can see at a glance what's on. **My journal**, **Status and log** and **Request uninstall** live at the bottom left, out of the wizard's way.

**Changing settings needs an administrator account.** On a standard account you can look around and preview everything; NightGuard shows an **Open as administrator** button for saving. (That's also the setup we recommend: a standard account day to day, and someone you trust keeps the admin password.)

Times are shown the same way as your Windows clock: 12-hour with AM/PM, or 24-hour. Each time box is labelled with the format it uses. Click the hour or minute and use the arrows, or just type.

| Step | What you can set | Default |
|---|---|---|
| **Bedtimes** | Bedtime and unlock time for each night. Untick nights to leave them free. Use the **Set several nights at once** box to give every night, school nights or weekend nights the same times in one click. Each row shows which hours get locked. | Every night, 00:00 to 06:00 |
| **Warnings** | Up to 6 popups: how many minutes before, the message, sound on or off. **Preview** any popup, or the bedtime countdown, right there. | 30 and 5 minutes before |
| **At bedtime** | Hibernate, Shutdown, Sleep, Lock or Sign out. Countdown length, how often it repeats, night mode length, end date. | Hibernate, 60 s countdown, repeats every minute |
| **Your why and journal** | Your reason, in your words (highly recommended). Morning check-in, streaks and badges on or off. | Check-in on |
| **Game and screen time** | Daily game and screen time budgets (weekdays and weekends), warnings, what happens when time runs out, which programs count as games | Off (1 h games / 4 h screen on weekdays, 2 h / 6 h at weekends when you turn it on) |
| **Block sites and apps** | Websites, apps or whole folders to block, and when | Off (a social media list is ready to go) |
| **Trusted person** | Turn on the code lock. Your trusted person types their own code when you save. | Off |
| **Review and save** | Check everything (including what counts as looser than now), desktop shortcut, confirm, save. | Start Menu shortcut |

**How nights are named:** after the evening they start. A bedtime before 12:00 counts as *after midnight*, so "Mon 00:30" means 12:30 AM, early Tuesday.

**Time zones:** your bedtimes follow the time zone the PC had when you last saved. Switching the PC's time zone at 11 PM doesn't move bedtime. Travelling? Save your settings again in the new time zone (moving west counts as looser, since bedtime then comes later in real time).

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
- **Hibernate has to be switched on in Windows.** It's off on many desktop PCs. When you save with Hibernate picked, NightGuard checks and, **if you say yes**, turns it on for you (`powercfg /hibernate on`). Windows then sets aside some disk space for it (a file about half the size of your RAM). If you say no, or your PC can't hibernate, NightGuard **shuts down instead**. Uninstalling NightGuard leaves the Windows hibernate setting as it is.

---

## ☀️ Morning check-in and journal

Quitting late nights works like quitting anything: small wins, counted. So on your **first sign-in each day**, NightGuard pops up with:

- **A verdict on last night.** Off before bedtime? Applause. NightGuard had to step in? A gentle roast and a fresh start. Free night? Hope it was fun.
- **Your streak:** clean nights in a row. A *clean night* means you were off before bedtime and stayed off, so the bouncer never had to step in. Free nights don't break the streak.
- **An optional mood (1 to 5) and a short note**, saved to your journal. Or click **Skip**. No guilt trips. Well, small ones.

### 🏅 Badges

Clean nights in a row earn badges, all the way to a year: **First Light** (1), **Hat Trick** (3), **High Five** (5), **Week Warrior** (7), **Double Digits** (10), **Fortnight Knight** (14), **Habit Hacker** (21), **Moonwalker** (30), **Sandman's Apprentice** (45), **Dream Team** (60), **Pillow Pro** (75), **Quarter Master** (90), **Night Owl, Retired** (120), **Sleep Scholar** (150), **Half-Year Hero** (180), **Snooze Sage** (225), **Nine-Month Ninja** (270), **Three Hundred Club** (300), **Almost There** (330) and the big one, **Sleep Legend** (365). The morning check-in announces each new one.

### 💬 Your why

On the **Your why and journal** step, write one or two sentences about why you're doing this. Optional, but **highly recommended**: at 11:55 PM mid-game, "be healthier" is easy to ignore, while "I want to stop being a zombie at work" hits different. It shows up in the final warning, the bedtime countdown, your morning check-in and your journal. Change it any time (no cool-off needed).

### 📓 My journal

Read it all back under **My journal** (bottom left in the app): your why, your streak, badges earned and what's next, your entries and your average mood lately. **Save a copy...** exports it to a text file.

Privacy: the journal stays on this PC, in a folder that only your Windows account (and the PC's administrators) can read. Nothing leaves the computer. It's deleted when NightGuard is uninstalled, so save a copy first if you want to keep it. Don't want the check-in? Untick **Morning check-in** on the At bedtime step (no cool-off needed for that one).

## 🎮 Screen time limits

Turn them on in the **Game and screen time** step.

- **Game time** counts while a game is running and you're at the screen. It warns you at 15, 5 and 1 minutes left. When it's used up, games (and optionally launchers like Steam) are closed, and closed again if you reopen them.
- **Screen time** counts every minute you're signed in and the screen isn't locked. When it runs out, NightGuard **locks** the PC (default), **shuts it down**, or just **nags** you, your choice.
- **The day resets at 4 AM**, not midnight, so that 1 AM "quick game" counts toward the evening it belongs to. Nice try.
- **What counts as a game?** Anything running from a game folder (Steam, Epic, Xbox, GOG, Riot, Ubisoft, EA and Battle.net folders are pre-filled), plus any extra program names you add. Add your own library folders, such as `D:\SteamLibrary\steamapps\common`.
- Windows' own programs are never counted or closed, and screen time can't be set below 30 minutes. Heads-up: once today's screen time is used up with **Lock** or **Shutdown**, the PC stays locked or off until the day resets (4 AM by default). That's the deal you made with yourself. Safe Mode still works if you ever really need in.

## 🔒 No easy take-backs (the self-lock)

NightGuard treats itself like a commitment, because "I'll just turn it off tonight" is exactly the problem.

- **Opening NightGuard again** shows that it's on duty, when its last night is, and what you can change. It never offers a quick uninstall, and **Uninstall** in Windows Settings is switched off (it points you back to the app).
- **Stricter changes save right away:** earlier bedtime, more nights, more blocked sites, less game time, a stronger bedtime action.
- **Looser changes need an unlock:** later bedtime, fewer nights, removing blocks, more game time, a weaker action, a longer countdown. NightGuard lists what's looser and offers to **request an unlock**.
- **The unlock has a 7-day cool-off.** After it, you have 3 days to make looser changes or uninstall, once. Then it locks again. You can cancel a request anytime.
- **Uninstall** works the same way: **Request uninstall** (bottom left) starts the cool-off. When it's over, you get one last **ARE YOU SURE?**
- **Set an end date?** Then there's no uninstall before it, and the date can't be moved earlier. NightGuard removes itself after the last night, as promised.
- **Trusted-person mode on?** Your trusted person's code skips the cool-off (for changes and uninstall).
- **Night mode** still applies: during the last hour before bedtime and through the night, nothing can be changed at all.

- **Closing the app does nothing.** The guarding is done by a Windows service. If the tray app is closed or killed, the service starts it again; if the service is stopped, a watchdog starts it again within 5 minutes, and Windows restarts it if it crashes.
- **Moving the clock forward** while NightGuard runs doesn't shorten the cool-off: the request moves forward by the same amount. Changing the time zone doesn't move bedtime (see [time zones](#-settings)).

Honest note: this is a speed bump for your 1 AM brain, not a prison. A Windows administrator can still [remove NightGuard by hand](#-removing-it-by-hand-last-resort). If you want it airtight, use a standard (non-admin) Windows account day to day and let someone you trust keep the admin password.

What a **standard account** can't do to dodge it: stop the service, change NightGuard's files or settings, change the clock, outrun it with the time zone (NightGuard pins the time zone of your last save), cancel the bedtime shutdown, or close the countdown to skip it. What still gets past it: Safe Mode (on purpose, so you're never truly locked out), changing the clock in the BIOS, and for app blocking, copying the app somewhere else.

## 🔐 Trusted-person mode

Someone you trust (a parent, partner, friend, or the roommate who's tired of hearing you game at 3 AM) picks their own 8-digit code. From then on, **changing any setting or uninstalling needs the code.**

How it works:

1. Tick **Use a trusted-person code** on the Trusted person step.
2. On the last step, NightGuard double-checks you really understand the risk before the Install button appears.
3. When you click **Start NightGuard** (or **Save and apply**), hand the keyboard to your trusted person. They type their code twice, hidden. **NightGuard never shows, emails or prints it**, so you never see it. That's the whole point.

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
- Blocks switch on and off on schedule (checked every minute) and are **removed automatically** when the time slot ends, on uninstall, or if anything goes wrong. Deleting the block by hand doesn't stick: it's put back within a minute.
- Windows-critical sites (Microsoft, Windows Update, sign-in, security certificates) and Windows' own folders can't be blocked, so you can't break your PC by accident.

**Browser loophole:** browsers with "secure DNS" (DNS over HTTPS) turned on can skip the hosts file. Turn it off to make site blocking stick:

- **Chrome:** Settings > Privacy and security > Security > *Use secure DNS* > off
- **Edge:** Settings > Privacy, search, and services > Security > *Use secure DNS* > off
- **Firefox:** Settings > Privacy & Security > *DNS over HTTPS* > Off

---

## 🧯 Safety: it won't brick your PC

NightGuard was built to be strict at night and harmless otherwise:

- It only creates **its own Windows service** (`NightGuard`), **one watchdog scheduled task** (in a `NightGuard` folder in Task Scheduler), **its own firewall rules** (group `NightGuard`), **one marked section in the hosts file**, its program folder `C:\Program Files\NightGuard`, its data folder `C:\ProgramData\NightGuard` (settings, log and journal live there), and shortcuts. The only Windows setting it may change is turning hibernate on, and only if you say yes. Nothing touches boot settings, drivers, accounts, or your network setup.
- Settings that could cause a shutdown loop are refused: a night can be at most 16 hours, and there must be at least 6 free hours between nights.
- There's always a countdown of at least 15 seconds before anything happens.
- **Damaged settings or a wrong clock?** NightGuard does nothing rather than risk a shutdown loop. If the settings file can't be read, or the clock is earlier than when the settings were saved (for example after a BIOS reset), it stands down and writes why in the log.
- **Safe Mode is always safe:** the service doesn't run in Safe Mode, and would do nothing there if it did.
- **Resume-safe.** Every countdown is timed by the real clock. If the PC goes to sleep or hibernates in the middle of one, that countdown is dropped when it wakes up, and a fresh check decides what (if anything) happens. So hibernating at midnight and switching on at 10 AM gives you a normal morning, not a surprise hibernate.
- If blocking hits an error, it removes the blocks rather than leaving things half-blocked.

### 🔧 Removing it by hand (last resort)

The normal way is **Request uninstall** at the bottom left of the app (7-day cool-off), or the trusted person's code. If you can't use those (lost code, something broken, a real emergency) and you're a Windows admin, open **PowerShell as administrator** and run:

```powershell
$p = Get-ChildItem HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall | Get-ItemProperty | Where-Object DisplayName -eq 'NightGuard'
Start-Process msiexec.exe -Wait -ArgumentList "/x $($p.PSChildName) NIGHTGUARD_UNINSTALL=1"
```

That runs the normal uninstaller, which also removes the service, the watchdog task, the blocks, the shortcuts and `C:\ProgramData\NightGuard`.

If even that fails, this removes everything step by step:

```powershell
Get-ScheduledTask -TaskPath '\NightGuard\' | Unregister-ScheduledTask -Confirm:$false
Stop-Service NightGuard -Force; sc.exe delete NightGuard
Get-Process NightGuard -ErrorAction SilentlyContinue | Stop-Process -Force
Get-NetFirewallRule -Group 'NightGuard' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$h = "$env:SystemRoot\System32\drivers\etc\hosts"
(Get-Content $h -Raw) -replace '(?ms)^# >>> NightGuard BEGIN.*?^# <<< NightGuard END[^\n]*\n?', '' | Set-Content $h -NoNewline
cmd /c rmdir /s /q "$env:ProgramData\NightGuard" "$env:ProgramFiles\NightGuard"
Remove-Item "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\NightGuard*.lnk", "$env:PUBLIC\Desktop\NightGuard.lnk" -ErrorAction SilentlyContinue
```

If it's during locked hours, boot into **Safe Mode** first (hold **Shift** while clicking **Restart** > Troubleshoot > Advanced options > Startup Settings > Restart > **4**) and run it there.

---

## 🧪 Try it before you trust it

NightGuard 2 is a fresh rewrite as a real Windows app. It's built carefully and its rules are covered by automated tests, but it hasn't been battle-tested on lots of PCs yet. Do the [test drive](#-not-sure-yet-test-drive-it-first) first: previews during the day, then a one-night trial with the end date set to today. It removes itself in the morning, and you install it for real with your settings dialled in.

Something weird? [Open an issue](../../issues). Bug reports from fellow procrastinators are very welcome (ideally filed before midnight 😉).

---

## 🗂️ What's in the box

NightGuard 2 is written in C# (.NET 10) and has three parts:

| Part | What it does |
|---|---|
| **NightGuard service** (`NightGuard.Service.exe`) | Runs as a Windows service (LocalSystem), always on, even before anyone signs in. Does all the enforcing: warnings, the bedtime countdown and action, site and app blocking, game and screen time, the end date. Checks every request against night mode, the self-lock and the trusted person's code. |
| **NightGuard app** (`NightGuard.exe`) | The settings window, and a small tray app the service starts for every signed-in user to show warnings, the countdown and the morning check-in. It never enforces anything itself, so closing it changes nothing. |
| **Installer** (`NightGuard.msi`) | Installs both, sets the service to restart itself if it fails, adds the Start Menu shortcut, and on uninstall removes everything NightGuard created. |

The service and the app talk over a local named pipe that only signed-in users can use. Settings are saved in `C:\ProgramData\NightGuard\config.json`, with a log in `log.txt` next to it (also under **Status and log** in the app). The format is the same as the PowerShell version's, so upgrades carry everything over.

Source layout: `src/NightGuard.Core` (all the rules, unit tested), `src/NightGuard.Service`, `src/NightGuard.App`, `installer/` (WiX), `tests/`. The original PowerShell version is kept in `legacy/` for reference.

**Upgrading from the PowerShell version, "Bedtime Lock" or the separate "Screen Limits" tool?** Just install the MSI. It carries over your settings (and today's usage) and removes the old versions.

### Building it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). The rules and tests build anywhere; the app, service and installer need Windows.

```powershell
dotnet test tests/NightGuard.Core.Tests
dotnet publish src/NightGuard.App -c Release -r win-x64 --self-contained -o out\publish
dotnet publish src/NightGuard.Service -c Release -r win-x64 --self-contained -o out\publish
dotnet build installer/NightGuard.Installer.wixproj -c Release -p:PublishDir=$PWD\out\publish\ -o out\msi
```

GitHub Actions does the same on every push and attaches `NightGuard.msi` to releases tagged `v*`.

---

## 🙋 FAQ

**I installed it and now I regret it. Help?**
Request an unlock (or uninstall) in the app and wait out the 7-day cool-off. If you still want out after a week of decent sleep, fair enough: you'll get the button. We're betting you won't. 😉

**Can I cancel a shutdown "just this once"?**
That's exactly the kind of thing NightGuard is here to stop. During the day you can make it stricter anytime; looser takes the 7-day cool-off.

**Does it spy on me or phone home?**
No. No internet, no accounts, no tracking. Your mood journal never leaves the PC. The source is all here to read.

**I'm travelling. Does it follow my time zone?**
NightGuard keeps the time zone you last saved your settings in (otherwise changing the time zone would be a one-click escape, and anyone can do that without admin rights). Moving for real? Change the Windows time zone, then save your settings again. Heading east is stricter (bedtime comes sooner) and saves right away; heading west counts as looser, so it needs the cool-off or your trusted person's code.

**I work night shifts.**
Set your own bedtime and unlock time per night. Any time works, as long as there are at least 6 free hours between nights.

**Will it close my game without saving?**
With Shutdown, yes, after the countdown. With Hibernate (the default) your game is usually still there when you switch the PC back on, but don't bet your save file on it. That's why there are warnings. Save at the 5-minute warning. 🙂

**Will the warnings show up over my fullscreen game?**
The bedtime countdown will. It takes over the whole screen your game is on, and if a game in *exclusive* fullscreen keeps it hidden, NightGuard minimises the game. Need to save? Click **Let me save first** and it shrinks to a small bar on top of the screen while you save; 10 seconds before the end it takes over the screen again (and closing it doesn't stop it). The earlier warnings are always-on-top popups that pull themselves to the front, with a sound and a Windows notification as backup. They get through *borderless* fullscreen (most games today); in *exclusive* fullscreen you'll at least hear them. Tip: switch your game to borderless or windowed fullscreen to see every warning.

**Can I delete the installer I downloaded?**
Yes. NightGuard is installed in `C:\Program Files\NightGuard` and runs from there.

**I closed the tray icon / killed NightGuard in Task Manager. Is it off now?**
No. The tray app only shows things; the Windows service does the guarding, and it starts the tray app again within seconds.

**Will it slow down my PC or my games?**
Nope. The service wakes up for a moment every few seconds to check the time, and once a minute for blocking and screen time; the rest of the time it sleeps. The tray app hands its memory back to Windows whenever its windows are closed. Your FPS won't notice. Your sleep schedule will.

**Why does it repeat every minute at night? Isn't that a lot?**
Each check is a blink. Every minute means that if you switch the PC back on at 2 AM, you get about a minute before it goes back to bed. You can make it less often on the At bedtime step, but where's the fun in that.

---

## 🤝 Contributing

Ideas, bug fixes and translations are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md). Found a security issue? See [SECURITY.md](SECURITY.md).

## 📄 License

NightGuard is **source-available** under the [PolyForm Strict License 1.0.0](LICENSE). In plain words:

- ✅ **Free to use** for personal and other noncommercial use (people, schools, charities and so on). Read the code all you like.
- 🚫 **No selling it, and no commercial use.**
- 🚫 **No modified versions or redistributing it.** Share the link to this page instead.
- 🤝 **Contributions welcome:** you may fork it only to send changes back here as a pull request (see [CONTRIBUTING.md](CONTRIBUTING.md)).

It's not "open source" in the OSI sense, on purpose: it stays free, and it stays Omid's.

Now close this tab and go to sleep. 🌙
