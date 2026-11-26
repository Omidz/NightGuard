namespace NightGuard.Core.Config;

// The settings file (C:\ProgramData\NightGuard\config.json). The format is the same one
// the PowerShell version wrote (version 3), plus TimeZone, so existing installs carry over.
//
//   Schedule          Per night (Mon..Sun = the evening the night starts): Enabled, Bedtime (HH:mm), Unlock (HH:mm).
//                     A bedtime before 12:00 means after midnight, e.g. Mon 00:30 = Tuesday 00:30.
//   Warnings          { MinutesBefore, Sound, Message }. Placeholders: {minutes} {bedtime} {unlock} {action}. \n = new line.
//   Action            Shutdown | Sleep | Hibernate | Lock | SignOut
//   CountdownSeconds  Notice before the action (15-600)
//   RecheckMinutes    How often the action repeats during the night (1-60)
//   NightModeMinutes  Settings and uninstall are blocked from this long before bedtime until unlock (0-240)
//   EndDate           yyyy-MM-dd: the last locked night. NightGuard removes itself after it. null = never.
//   Why               Your reason, in your words
//   Plan              Your if-then plan for the final warning ("When the 5-minute warning pops up, I will ...")
//   CommitNights      How many nights you committed to (0 = forever, null = no commitment yet). A goal, not a lock.
//   CommitStart       yyyy-MM-dd: the day that commitment started
//   MorningCheckIn    Morning check-in, streaks, badges and journal
//   Blocking          Sites (hosts file) and apps (Windows Firewall), during bedtime and/or time slots
//   ScreenTime        Daily game and screen time budgets
//   TimeZone          Windows time zone id the schedule is pinned to. Changing the PC's time zone
//                     at 11 PM doesn't move bedtime; saving the settings does (and moving west counts as looser).

public sealed class NightSchedule
{
    public bool Enabled { get; set; } = true;
    public string Bedtime { get; set; } = "00:00";
    public string Unlock { get; set; } = "06:00";
}

public sealed class WarningSetting
{
    public int MinutesBefore { get; set; }
    public bool Sound { get; set; } = true;
    public string Message { get; set; } = "";
}

public sealed class BlockWindow
{
    public List<string> Days { get; set; } = [];
    public string From { get; set; } = "09:00";
    public string To { get; set; } = "17:00";
}

public sealed class BlockingSettings
{
    public bool Enabled { get; set; }
    public bool DuringBedtime { get; set; } = true;
    public List<string> Sites { get; set; } = [.. Defaults.SocialPreset];
    public List<string> Apps { get; set; } = [];
    public List<BlockWindow> Windows { get; set; } = [];
}

public sealed class ScreenTimeSettings
{
    public bool Enabled { get; set; }
    public int ResetHour { get; set; } = 4;
    public int GameMinutesWeekday { get; set; } = 60;
    public int GameMinutesWeekend { get; set; } = 120;
    public int ScreenMinutesWeekday { get; set; } = 240;
    public int ScreenMinutesWeekend { get; set; } = 360;
    public List<int> WarnAtMinutesLeft { get; set; } = [15, 5, 1];
    public string LimitAction { get; set; } = "Lock";
    public int ShutdownGraceMinutes { get; set; } = 2;
    public bool CloseLaunchers { get; set; } = true;
    public List<string> GameFolders { get; set; } = [.. Defaults.GameFolders];
    public List<string> GameProcesses { get; set; } = [.. Defaults.GameProcesses];
    public List<string> IgnoreProcesses { get; set; } = [.. Defaults.IgnoreProcesses];
    public List<string> LauncherProcesses { get; set; } = [.. Defaults.LauncherProcesses];
}

public sealed class NightGuardConfig
{
    public int Version { get; set; } = 4;
    public Dictionary<string, NightSchedule> Schedule { get; set; } = Defaults.NewSchedule();
    public List<WarningSetting> Warnings { get; set; } = Defaults.NewWarnings();
    public string Action { get; set; } = "Hibernate";
    public int CountdownSeconds { get; set; } = 60;
    public int RecheckMinutes { get; set; } = 1;
    public int NightModeMinutes { get; set; } = 60;
    public string? EndDate { get; set; }
    public bool MorningCheckIn { get; set; } = true;
    public string Why { get; set; } = "";
    public string Plan { get; set; } = "";
    public int? CommitNights { get; set; }
    public string? CommitStart { get; set; }
    public bool ShortcutDesktop { get; set; }
    public BlockingSettings Blocking { get; set; } = new();
    public ScreenTimeSettings ScreenTime { get; set; } = new();
    public string? TimeZone { get; set; }

    public NightSchedule Night(string day) =>
        Schedule.TryGetValue(day, out var s) ? s : new NightSchedule { Enabled = false };

    public NightGuardConfig Clone() => ConfigSerializer.Read(ConfigSerializer.Write(this));
}
