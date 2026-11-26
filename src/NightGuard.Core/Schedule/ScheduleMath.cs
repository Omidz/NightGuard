using System.Globalization;
using System.Text.RegularExpressions;
using NightGuard.Core.Config;

namespace NightGuard.Core.Schedule;

/// <summary>One locked night. Evening is the date the night is named after.</summary>
public sealed record Night(string Day, DateTime Evening, DateTime Start, DateTime End);

/// <summary>
/// All schedule maths. Every "now" here is wall-clock time in the schedule's time zone
/// (see <see cref="ScheduleClock"/>); nothing in here reads the system clock.
/// </summary>
public static partial class ScheduleMath
{
    [GeneratedRegex(@"^([01]?\d|2[0-3]):[0-5]\d$")]
    private static partial Regex TimeRegex();

    public static bool IsTime(string? s) => s is not null && TimeRegex().IsMatch(s);

    public static TimeSpan ParseTime(string s) => TimeSpan.ParseExact(s.Length == 4 ? "0" + s : s, @"hh\:mm", CultureInfo.InvariantCulture);

    public static DateTime? EndDate(NightGuardConfig cfg) =>
        cfg.EndDate is { } e && DateTime.TryParseExact(e, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>The night that starts on the evening of <paramref name="evening"/>, or null if that night is off.</summary>
    public static Night? GetNight(NightGuardConfig cfg, DateTime evening, bool ignoreEndDate = false)
    {
        var key = Defaults.DayKey(evening);
        var s = cfg.Night(key);
        if (!s.Enabled || !IsTime(s.Bedtime) || !IsTime(s.Unlock)) return null;
        if (!ignoreEndDate && EndDate(cfg) is { } end && evening.Date > end) return null;
        var bt = ParseTime(s.Bedtime);
        var ut = ParseTime(s.Unlock);
        var start = evening.Date + bt;
        if (bt.TotalHours < 12) start = start.AddDays(1);   // after midnight
        var stop = evening.Date.AddDays(1) + ut;
        if (stop <= start) return null;
        return new Night(key, evening.Date, start, stop);
    }

    public static IEnumerable<Night> Nights(NightGuardConfig cfg, DateTime now)
    {
        for (var i = -1; i <= 7; i++)
            if (GetNight(cfg, now.Date.AddDays(i)) is { } n) yield return n;
    }

    public static Night? CurrentNight(NightGuardConfig cfg, DateTime now) =>
        Nights(cfg, now).FirstOrDefault(n => now >= n.Start && now < n.End);

    public static Night? NextNight(NightGuardConfig cfg, DateTime now) =>
        Nights(cfg, now).Where(n => n.Start > now).OrderBy(n => n.Start).FirstOrDefault();

    /// <summary>When the lock stops for good (null = never).</summary>
    public static DateTime? Expiry(NightGuardConfig cfg)
    {
        if (EndDate(cfg) is not { } end) return null;
        return GetNight(cfg, end)?.End ?? end.AddDays(1);
    }

    public static bool IsExpired(NightGuardConfig cfg, DateTime now) => Expiry(cfg) is { } x && now >= x;

    /// <summary>A message if settings changes and uninstall are blocked right now (night mode), else null.</summary>
    public static string? NightModeReason(NightGuardConfig cfg, DateTime now)
    {
        if (IsExpired(cfg, now)) return null;
        foreach (var n in Nights(cfg, now))
        {
            var from = n.Start.AddMinutes(-cfg.NightModeMinutes);
            if (now >= from && now < n.End)
                return $"Night mode is on. Settings and uninstall are locked from {from:HH:mm} until {n.End:ddd HH:mm}. Go to bed, and decide tomorrow.";
        }
        return null;
    }

    /// <summary>Is site and app blocking active at <paramref name="now"/>?</summary>
    public static bool IsBlockActive(NightGuardConfig cfg, DateTime now)
    {
        var b = cfg.Blocking;
        if (!b.Enabled) return false;
        if (b.DuringBedtime && CurrentNight(cfg, now) is not null) return true;
        var today = Defaults.DayKey(now);
        var yday = Defaults.DayKey(now.AddDays(-1));
        var t = now.TimeOfDay;
        foreach (var w in b.Windows)
        {
            if (!IsTime(w.From) || !IsTime(w.To)) continue;
            var f = ParseTime(w.From);
            var to = ParseTime(w.To);
            var days = w.Days;
            if (f < to)
            {
                if (days.Contains(today) && t >= f && t < to) return true;
            }
            else if ((days.Contains(today) && t >= f) || (days.Contains(yday) && t < to))
            {
                return true;   // slot crosses midnight: belongs to the day it starts
            }
        }
        return false;
    }

    /// <summary>Fills in {minutes} {bedtime} {unlock} {action} and \n.</summary>
    public static string FormatMessage(string text, NightGuardConfig cfg, Night night, int minutes, string timeFormat = "HH:mm") =>
        text.Replace("\\n", "\n")
            .Replace("{minutes}", minutes.ToString(CultureInfo.InvariantCulture))
            .Replace("{bedtime}", night.Start.ToString(timeFormat, CultureInfo.CurrentCulture))
            .Replace("{unlock}", night.End.ToString(timeFormat, CultureInfo.CurrentCulture))
            .Replace("{action}", Defaults.ActionWord(cfg.Action));

    /// <summary>Hours locked for a single night row (for the schedule editor), or null if unlock is not after bedtime.</summary>
    public static double? LockedHours(string bedtime, string unlock)
    {
        if (!IsTime(bedtime) || !IsTime(unlock)) return null;
        var bt = ParseTime(bedtime).TotalHours;
        var ut = ParseTime(unlock).TotalHours;
        var start = bt < 12 ? bt + 24 : bt;
        var hours = 24 + ut - start;
        return hours > 0 ? hours : null;
    }
}

/// <summary>
/// Turns real time into schedule time. The schedule is pinned to a time zone, so changing the
/// PC's time zone late at night does not move bedtime.
/// </summary>
public static class ScheduleClock
{
    public static TimeZoneInfo Zone(NightGuardConfig cfg)
    {
        if (cfg.TimeZone is { } id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    public static DateTime Now(NightGuardConfig cfg, DateTime utcNow) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), Zone(cfg)), DateTimeKind.Unspecified);
}
