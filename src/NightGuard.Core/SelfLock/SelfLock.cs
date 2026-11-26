using NightGuard.Core.Config;
using NightGuard.Core.Schedule;

namespace NightGuard.Core.SelfLock;

// Self-lock: NightGuard treats itself like a commitment, not a setting.
//  - Making the rules STRICTER is always allowed (outside night mode).
//  - Making them LOOSER, or uninstalling, needs an unlock request and a 7-day cool-off.
//    After that the unlock stays open for 3 days, and it is used up by the first
//    loosening save or uninstall.
//  - With an end date, uninstall is not possible before it (NightGuard removes itself
//    after the last night), and the end date can't be moved earlier.
//  - The trusted person's code skips all of this.
//  - A Windows admin can still remove NightGuard by hand (see the README).

public enum UnlockStateKind { None, Waiting, Open }

public sealed record UnlockInfo(UnlockStateKind State, DateTime? RequestedAt, DateTime? OpensAt, DateTime? ClosesAt)
{
    public static readonly UnlockInfo None = new(UnlockStateKind.None, null, null, null);

    public string Describe() => State switch
    {
        UnlockStateKind.Waiting => $"Unlock requested. Cool-off ends {OpensAt:dddd d MMM, HH:mm}.",
        UnlockStateKind.Open => $"Unlocked until {ClosesAt:dddd d MMM, HH:mm}. Looser settings or uninstall are allowed once.",
        _ => "Locked in. Stricter changes are fine anytime; looser ones need an unlock request (7-day cool-off).",
    };
}

public static class SelfLockRules
{
    public const int CoolOffDays = 7;
    public const int UnlockOpenDays = 3;

    /// <summary>Where an unlock request stands. Times are local wall-clock times.</summary>
    public static UnlockInfo Unlock(DateTime? requestedAt, DateTime now)
    {
        if (requestedAt is not { } req) return UnlockInfo.None;
        if (req > now.AddDays(1)) return UnlockInfo.None;   // requested "in the future": the clock was changed, ignore it
        var opens = req.AddDays(CoolOffDays);
        var closes = opens.AddDays(UnlockOpenDays);
        var state = now < opens ? UnlockStateKind.Waiting : now < closes ? UnlockStateKind.Open : UnlockStateKind.None;
        return new UnlockInfo(state, req, opens, closes);
    }

    // Bedtime action strength: Shutdown is the strongest, then Hibernate, then Sign out / Sleep, then Lock.
    // Moving to the same or a stronger action is always allowed; a weaker one counts as looser.
    private static readonly Dictionary<string, int> ActionRank = new() { ["Shutdown"] = 4, ["Hibernate"] = 3, ["SignOut"] = 2, ["Sleep"] = 2, ["Lock"] = 1 };

    /// <summary>How strong a bedtime action is (higher = stricter). Unknown actions are 0.</summary>
    public static int ActionStrength(string action) => Rank(ActionRank, action);
    private static readonly Dictionary<string, int> ScreenActionRank = new() { ["Shutdown"] = 3, ["Lock"] = 2, ["Warn"] = 1 };

    private static int Rank(Dictionary<string, int> table, string key) => table.TryGetValue(key, out var r) ? r : 0;

    /// <summary>Every way <paramref name="newer"/> is looser than <paramref name="old"/>. Empty = same or stricter.</summary>
    /// <param name="utcNow">Used to compare time zones; null skips that check.</param>
    public static List<string> Loosening(NightGuardConfig old, NightGuardConfig newer, DateTime? utcNow = null)
    {
        var output = new List<string>();
        foreach (var d in Defaults.Days)
        {
            var o = old.Night(d);
            var n = newer.Night(d);
            if (!o.Enabled) continue;
            if (!n.Enabled) { output.Add($"{d} night is no longer locked"); continue; }
            if (!ScheduleMath.IsTime(o.Bedtime) || !ScheduleMath.IsTime(o.Unlock) || !ScheduleMath.IsTime(n.Bedtime) || !ScheduleMath.IsTime(n.Unlock)) continue;
            var (os, oe) = Span(o);
            var (ns, ne) = Span(n);
            if (ns > os) output.Add($"{d} night: later bedtime");
            if (ne < oe) output.Add($"{d} night: earlier unlock");
        }
        if (Rank(ActionRank, newer.Action) < Rank(ActionRank, old.Action)) output.Add($"bedtime action: {old.Action} to {newer.Action}");
        if (newer.CountdownSeconds > old.CountdownSeconds) output.Add("longer countdown");
        if (newer.RecheckMinutes > old.RecheckMinutes) output.Add("repeats less often at night");
        if (newer.NightModeMinutes < old.NightModeMinutes) output.Add("shorter night mode");
        var oEnd = ScheduleMath.EndDate(old);
        var nEnd = ScheduleMath.EndDate(newer);
        if (nEnd is { } ne2 && (oEnd is null || ne2 < oEnd)) output.Add(oEnd is null ? "new end date" : "earlier end date");

        // Moving the schedule's time zone west makes bedtime come later in real time
        if (utcNow is { } utc && !string.Equals(old.TimeZone, newer.TimeZone, StringComparison.OrdinalIgnoreCase))
        {
            var oldOffset = ScheduleClock.Zone(old).GetUtcOffset(utc);
            var newOffset = ScheduleClock.Zone(newer).GetUtcOffset(utc);
            if (newOffset < oldOffset) output.Add("time zone moved west (bedtime comes later in real time)");
        }

        var ob = old.Blocking;
        var nb = newer.Blocking;
        if (ob.Enabled)
        {
            if (!nb.Enabled) output.Add("blocking turned off");
            else
            {
                if (ob.DuringBedtime && !nb.DuringBedtime) output.Add("no blocking during bedtime");
                var goneSites = ob.Sites.Where(s => !nb.Sites.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
                if (goneSites.Count > 0) output.Add("sites unblocked: " + string.Join(", ", goneSites));
                var goneApps = ob.Apps.Where(s => !nb.Apps.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
                if (goneApps.Count > 0) output.Add("apps unblocked: " + string.Join(", ", goneApps));
                // Compare the minutes of the week each set of time slots covers: adding days or making a
                // slot longer is stricter; any minute that was blocked and no longer is counts as looser.
                var before = BlockedMinutes(ob.Windows);
                var after = BlockedMinutes(nb.Windows);
                for (var m = 0; m < before.Length; m++)
                    if (before[m] && !after[m]) { output.Add("blocking time slots cover less time"); break; }
            }
        }

        var ost = old.ScreenTime;
        var nst = newer.ScreenTime;
        if (ost.Enabled)
        {
            if (!nst.Enabled) output.Add("screen time limits turned off");
            else
            {
                void More(int o, int n, string what) { if (o > 0 && (n == 0 || n > o)) output.Add("more " + what); }
                More(ost.GameMinutesWeekday, nst.GameMinutesWeekday, "game time on weekdays");
                More(ost.GameMinutesWeekend, nst.GameMinutesWeekend, "game time at weekends");
                More(ost.ScreenMinutesWeekday, nst.ScreenMinutesWeekday, "screen time on weekdays");
                More(ost.ScreenMinutesWeekend, nst.ScreenMinutesWeekend, "screen time at weekends");
                if (Rank(ScreenActionRank, nst.LimitAction) < Rank(ScreenActionRank, ost.LimitAction)) output.Add($"screen time action: {ost.LimitAction} to {nst.LimitAction}");
                if (nst.ResetHour != ost.ResetHour) output.Add("different day reset hour");
                if (nst.ShutdownGraceMinutes > ost.ShutdownGraceMinutes) output.Add("longer shutdown notice");
                if (ost.CloseLaunchers && !nst.CloseLaunchers) output.Add("launchers no longer closed");
                if (Removed(ost.GameFolders, nst.GameFolders)) output.Add("removed from GameFolders");
                if (Removed(ost.GameProcesses, nst.GameProcesses)) output.Add("removed from GameProcesses");
                if (Removed(ost.LauncherProcesses, nst.LauncherProcesses)) output.Add("removed from LauncherProcesses");
                if (Removed(nst.IgnoreProcesses, ost.IgnoreProcesses)) output.Add("more programs ignored by screen time");
                // Fewer warnings is fine; it doesn't loosen the limit itself
            }
        }
        return output;
    }

    /// <summary>Which minutes of the week (Monday 00:00 = 0) the time slots block.</summary>
    internal static bool[] BlockedMinutes(IEnumerable<BlockWindow> windows)
    {
        const int week = 7 * 1440;
        var covered = new bool[week];
        foreach (var w in windows)
        {
            if (!ScheduleMath.IsTime(w.From) || !ScheduleMath.IsTime(w.To)) continue;
            var from = (int)ScheduleMath.ParseTime(w.From).TotalMinutes;
            var to = (int)ScheduleMath.ParseTime(w.To).TotalMinutes;
            var length = to > from ? to - from : 1440 - from + to;   // a slot like 22:00-02:00 crosses midnight
            foreach (var d in w.Days)
            {
                var day = Array.IndexOf(Defaults.Days, d);
                if (day < 0) continue;
                for (var m = 0; m < length; m++) covered[(day * 1440 + from + m) % week] = true;
            }
        }
        return covered;
    }

    private static bool Removed(List<string> before, List<string> after) =>
        before.Any(x => !after.Contains(x, StringComparer.OrdinalIgnoreCase));

    // Minutes from the evening's midnight: start (bedtime) and end (unlock) of a night
    private static (double Start, double End) Span(NightSchedule s)
    {
        var b = ScheduleMath.ParseTime(s.Bedtime).TotalMinutes;
        var u = ScheduleMath.ParseTime(s.Unlock).TotalMinutes;
        return (b < 720 ? b + 1440 : b, u + 1440);
    }

    /// <summary>Why uninstalling isn't allowed right now (ignoring the trusted code), or null if it is.</summary>
    public static string? UninstallBlock(NightGuardConfig? cfg, UnlockInfo unlock)
    {
        if (cfg is null) return null;   // damaged or missing settings: never trap anyone
        if (ScheduleMath.EndDate(cfg) is { } end)
            return $"NightGuard is locked in until its last night, {end:dddd d MMMM yyyy}. After that it removes itself.";
        return unlock.State switch
        {
            UnlockStateKind.Open => null,
            UnlockStateKind.Waiting => $"You asked to uninstall. The 7-day cool-off ends {unlock.OpensAt:dddd d MMM, HH:mm}. Still want out then? Come back.",
            _ => "NightGuard is locked in. To uninstall, request an unlock. After a 7-day cool-off you can uninstall.",
        };
    }
}
