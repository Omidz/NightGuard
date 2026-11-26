namespace NightGuard.Core.Config;

public static class Defaults
{
    public const string AppName = "NightGuard";

    public static readonly string[] Days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    public static readonly string[] DayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    public static readonly string[] Actions = ["Shutdown", "Sleep", "Hibernate", "Lock", "SignOut"];
    public static readonly string[] ScreenActions = ["Lock", "Shutdown", "Warn"];

    public static readonly IReadOnlyDictionary<string, string> ActionWords = new Dictionary<string, string>
    {
        ["Shutdown"] = "shut down", ["Sleep"] = "go to sleep", ["Hibernate"] = "hibernate", ["Lock"] = "lock", ["SignOut"] = "sign you out",
    };

    public static string ActionWord(string action) => ActionWords.TryGetValue(action, out var w) ? w : action.ToLowerInvariant();

    public static readonly string[] SocialPreset =
        ["instagram.com", "facebook.com", "tiktok.com", "x.com", "twitter.com", "reddit.com", "youtube.com", "snapchat.com", "twitch.tv", "pinterest.com"];

    // Never block these: Windows, updates, security and sign-in would break
    public static readonly string[] ProtectedDomains =
    [
        "microsoft.com", "windows.com", "windowsupdate.com", "live.com", "microsoftonline.com", "msftconnecttest.com",
        "msftncsi.com", "office.com", "office.net", "azure.com", "digicert.com", "verisign.com", "globalsign.com",
        "letsencrypt.org", "localhost", "anthropic.com", "claude.ai",
    ];

    // Never counted as games and never closed, whatever the settings say
    public static readonly string[] NeverKill =
    [
        "explorer", "dwm", "csrss", "winlogon", "wininit", "lsass", "services", "svchost", "smss", "logonui",
        "taskmgr", "powershell", "pwsh", "cmd", "conhost", "sihost", "fontdrvhost", "ctfmon", "runtimebroker",
        "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost", "audiodg", "msmpeng", "securityhealthsystray",
        "nightguard", "nightguard.service", "msiexec", "consent", "userinit", "winlogon",
    ];

    public static readonly string[] GameFolders =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common", @"C:\Program Files\Steam\steamapps\common",
        @"D:\SteamLibrary\steamapps\common", @"E:\SteamLibrary\steamapps\common",
        @"C:\Program Files\Epic Games", @"D:\Epic Games", @"C:\XboxGames", @"D:\XboxGames",
        @"C:\Program Files (x86)\GOG Galaxy\Games", @"C:\GOG Games", @"C:\Riot Games",
        @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games", @"C:\Program Files\EA Games",
        @"C:\Program Files (x86)\Battle.net\Games",
    ];

    public static readonly string[] GameProcesses =
    [
        "League of Legends", "VALORANT-Win64-Shipping", "FortniteClient-Win64-Shipping", "cs2", "dota2",
        "RobloxPlayerBeta", "Minecraft.Windows", "javaw", "GTA5", "RocketLeague", "r5apex", "Overwatch", "eldenring",
    ];

    public static readonly string[] IgnoreProcesses =
    [
        "steam", "steamwebhelper", "steamservice", "EpicGamesLauncher", "EpicWebHelper", "UnrealCEFSubProcess",
        "CrashReportClient", "UnityCrashHandler64", "UnityCrashHandler32", "GameBar", "GameBarFTServer", "XboxPcApp",
        "WindowsTerminal", "Microsoft.Photos", "Calculator", "CalculatorApp", "Notepad", "msedge", "Spotify", "WhatsApp", "Teams", "ms-teams",
    ];

    public static readonly string[] LauncherProcesses =
    [
        "steam", "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper", "GalaxyClient", "Battle.net",
        "EADesktop", "upc", "RiotClientServices", "XboxPcApp",
    ];

    public const string Warning30 =
        @"Bedtime in {minutes} minutes.\n\nThe PC will {action} at {bedtime} and stays locked until {unlock}.\n\nFinish your match, save your work, and start winding down.";
    public const string Warning5 =
        @"FINAL WARNING\n\nThe PC will {action} at {bedtime} (about {minutes} min).\n\nSave your work and quit your game NOW.";
    public const string WarningGeneric = "Bedtime in {minutes} minutes. The PC will {action} at {bedtime}.";

    public static readonly (int MinMinutes, string Message)[] WarningExamples =
    [
        (30, @"Heads up! Bedtime in {minutes} minutes.\nThe PC will {action} at {bedtime}. Time to start wrapping up."),
        (10, @"{minutes} minutes to go.\nFinish this round and save your stuff. No starting new ones. Yes, we know."),
        (0, @"FINAL WARNING!\nThe PC will {action} at {bedtime}. Save NOW.\nFuture you says thanks."),
    ];

    public static Dictionary<string, NightSchedule> NewSchedule()
    {
        var d = new Dictionary<string, NightSchedule>(StringComparer.OrdinalIgnoreCase);
        foreach (var day in Days) d[day] = new NightSchedule();
        return d;
    }

    public static List<WarningSetting> NewWarnings() =>
    [
        new() { MinutesBefore = 30, Sound = true, Message = Warning30 },
        new() { MinutesBefore = 5, Sound = true, Message = Warning5 },
    ];

    public static int DayIndex(DateTime date) => ((int)date.DayOfWeek + 6) % 7;
    public static string DayKey(DateTime date) => Days[DayIndex(date)];
}
