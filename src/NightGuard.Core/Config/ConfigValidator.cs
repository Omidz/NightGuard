using System.Globalization;
using System.Text.RegularExpressions;
using NightGuard.Core.Blocking;
using NightGuard.Core.Schedule;

namespace NightGuard.Core.Config;

/// <summary>Checks a config. An empty list means it's fine.</summary>
public static partial class ConfigValidator
{
    [GeneratedRegex(@"^[\w .+()\-]{1,100}$")]
    private static partial Regex ProcRegex();

    public static List<string> Validate(NightGuardConfig cfg, PathRules? paths = null)
    {
        paths ??= PathRules.ForThisPc();
        var errs = new List<string>();
        var anyOn = false;
        foreach (var d in Defaults.Days)
        {
            if (!cfg.Schedule.TryGetValue(d, out var s)) { errs.Add($"{d} is missing from the schedule."); continue; }
            if (!ScheduleMath.IsTime(s.Bedtime)) errs.Add($"{d} bedtime '{s.Bedtime}' is not a time like 23:30.");
            if (!ScheduleMath.IsTime(s.Unlock)) errs.Add($"{d} unlock time '{s.Unlock}' is not a time like 06:00.");
            if (s.Enabled) anyOn = true;
        }
        if (!anyOn) errs.Add("Every night is turned off. Turn on at least one night, or uninstall instead.");
        if (cfg.Why.Length > 300) errs.Add("\"Why I started\" can be at most 300 characters.");
        if (cfg.Plan.Length > 200) errs.Add("Your plan can be at most 200 characters.");
        if (cfg.CommitNights is < 0 or > 3650) errs.Add("A commitment can be 1 to 3650 nights, or forever.");
        if (cfg.CommitStart is { } cs && !DateTime.TryParseExact(cs, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errs.Add($"Commitment start '{cs}' is not a date like 2027-01-31.");
        if (errs.Count == 0)
        {
            var reference = new DateTime(2024, 1, 1);   // a Monday
            for (var i = 0; i < 7; i++)
            {
                var day = Defaults.Days[i];
                var s = cfg.Schedule[day];
                var n = ScheduleMath.GetNight(cfg, reference.AddDays(i), ignoreEndDate: true);
                if (s.Enabled && n is null) { errs.Add($"{day}: unlock time {s.Unlock} must be after bedtime {s.Bedtime}."); continue; }
                if (n is null) continue;
                if ((n.End - n.Start).TotalHours > 16) errs.Add($"{day}: the locked time is longer than 16 hours.");
                // Safety: always leave at least 6 free hours before the next night starts
                var next = ScheduleMath.GetNight(cfg, reference.AddDays(i + 1), ignoreEndDate: true);
                if (next is not null && (next.Start - n.End).TotalHours < 6)
                    errs.Add($"{day} night ends at {n.End:HH:mm} but the next night starts at {next.Start:HH:mm}. Leave at least 6 free hours.");
            }
        }

        if (cfg.Warnings.Count > 6) errs.Add("At most 6 warnings are allowed.");
        foreach (var w in cfg.Warnings)
        {
            if (w.MinutesBefore < 1 || w.MinutesBefore > 360) errs.Add($"Warning minutes must be between 1 and 360 (got {w.MinutesBefore}).");
            if (w.Message.Length > 1000) errs.Add("A warning message can be at most 1000 characters.");
        }
        foreach (var g in cfg.Warnings.GroupBy(w => w.MinutesBefore).Where(g => g.Count() > 1))
            errs.Add($"Two warnings are set for {g.Key} minutes before bedtime.");
        if (!Defaults.Actions.Contains(cfg.Action)) errs.Add($"Action must be one of: {string.Join(", ", Defaults.Actions)}.");
        if (cfg.CountdownSeconds is < 15 or > 600) errs.Add("Countdown must be 15 to 600 seconds.");
        if (cfg.RecheckMinutes is < 1 or > 60) errs.Add("Repeat interval must be 1 to 60 minutes.");
        if (cfg.NightModeMinutes is < 0 or > 240) errs.Add("Night mode must start 0 to 240 minutes before bedtime.");
        if (cfg.EndDate is { } ed && !DateTime.TryParseExact(ed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errs.Add($"End date '{ed}' is not a date like 2027-01-31.");

        var b = cfg.Blocking;
        if (b.Sites.Count > 300) errs.Add("At most 300 blocked sites are allowed.");
        foreach (var site in b.Sites)
        {
            if (!DomainRules.IsValid(site)) errs.Add($"Blocked site '{site}' is not a site name like instagram.com.");
            else if (DomainRules.IsProtected(site)) errs.Add($"'{site}' can't be blocked, because Windows needs it.");
        }
        if (b.Apps.Count > 50) errs.Add("At most 50 blocked apps or folders are allowed.");
        foreach (var a in b.Apps)
        {
            if (!PathRules.IsFullPath(a)) errs.Add($"Blocked app '{a}' must be a full path like C:\\Games\\Steam.");
            else if (paths.IsProtected(a)) errs.Add($"'{a}' can't be blocked, because Windows needs it.");
        }
        if (b.Windows.Count > 20) errs.Add("At most 20 blocking time slots are allowed.");
        foreach (var w in b.Windows)
        {
            if (w.Days.Count == 0) errs.Add($"A blocking time slot ({w.From}-{w.To}) has no days ticked.");
            foreach (var d in w.Days) if (!Defaults.Days.Contains(d)) errs.Add($"Unknown day '{d}' in a blocking time slot.");
            if (!ScheduleMath.IsTime(w.From) || !ScheduleMath.IsTime(w.To)) errs.Add($"Blocking time slot '{w.From}-{w.To}' needs times like 09:00.");
            else if (ScheduleMath.ParseTime(w.From) == ScheduleMath.ParseTime(w.To)) errs.Add($"Blocking time slot {w.From}-{w.To} starts and ends at the same time.");
        }

        var st = cfg.ScreenTime;
        if (st.Enabled)
        {
            if (st.ResetHour is < 0 or > 23) errs.Add("Screen time: the new day must start at an hour from 0 to 23.");
            foreach (var v in new[] { st.GameMinutesWeekday, st.GameMinutesWeekend })
                if (v != 0 && (v < 5 || v > 1440)) errs.Add($"Screen time: game time must be 0 (no limit) or 5 to 1440 minutes (got {v}).");
            // Safety: at least 30 minutes, so the PC is never unusable for a whole day
            foreach (var v in new[] { st.ScreenMinutesWeekday, st.ScreenMinutesWeekend })
                if (v != 0 && (v < 30 || v > 1440)) errs.Add($"Screen time: screen time must be 0 (no limit) or 30 to 1440 minutes (got {v}).");
            if (st.WarnAtMinutesLeft.Count > 6) errs.Add("Screen time: at most 6 warnings.");
            foreach (var m in st.WarnAtMinutesLeft)
                if (m is < 1 or > 120) errs.Add($"Screen time: warnings must be 1 to 120 minutes before the limit (got {m}).");
            if (!Defaults.ScreenActions.Contains(st.LimitAction)) errs.Add($"Screen time: the action must be one of: {string.Join(", ", Defaults.ScreenActions)}.");
            if (st.ShutdownGraceMinutes is < 1 or > 10) errs.Add("Screen time: shutdown notice must be 1 to 10 minutes.");
            if (st.GameFolders.Count > 50) errs.Add("Screen time: at most 50 game folders.");
            foreach (var f in st.GameFolders)
            {
                if (!PathRules.IsFullPath(f)) errs.Add($"Screen time: game folder '{f}' must be a full path.");
                else if (paths.IsProtected(f)) errs.Add($"Screen time: '{f}' can't be a game folder, because Windows runs from there.");
            }
            foreach (var (name, list) in new[] { ("GameProcesses", st.GameProcesses), ("IgnoreProcesses", st.IgnoreProcesses), ("LauncherProcesses", st.LauncherProcesses) })
            {
                if (list.Count > 200) errs.Add($"Screen time: at most 200 entries in {name}.");
                foreach (var n in list)
                {
                    if (!ProcRegex().IsMatch(n)) errs.Add($"Screen time: '{n}' is not a program name (use the name from Task Manager, without .exe).");
                    else if (name != "IgnoreProcesses" && Defaults.NeverKill.Contains(n.ToLowerInvariant()))
                        errs.Add($"Screen time: '{n}' is part of Windows and can't be treated as a game or launcher.");
                }
            }
        }
        return errs;
    }

    /// <summary>
    /// Problems that only matter when saving (not when enforcing an already-saved config).
    /// </summary>
    public static List<string> ValidateForSave(NightGuardConfig cfg, DateTime scheduleNow, PathRules? paths = null)
    {
        var errs = Validate(cfg, paths);
        if (errs.Count == 0 && ScheduleMath.IsExpired(cfg, scheduleNow)) errs.Add($"The end date {cfg.EndDate} has already passed.");
        return errs;
    }
}
