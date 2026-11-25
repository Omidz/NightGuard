using System.Globalization;
using NightGuard.Core.Config;
using NightGuard.Core.Schedule;

namespace NightGuard.Core.Journal;

public sealed class JournalEntry
{
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public int Mood { get; set; }
    public string Note { get; set; } = "";
    public string LastNight { get; set; } = "";
    public int Streak { get; set; }
    public string Badge { get; set; } = "";
}

public enum NightKind { New, Clean, SteppedIn, Free }

public sealed record NightReport(NightKind Kind, int Streak, int Best);

public sealed record Badge(int Nights, string Name, string Line);

public static class Badges
{
    public static readonly Badge[] All =
    [
        new(1, "First Light", "One clean night. Every legend starts somewhere."),
        new(3, "Hat Trick", "Three nights. Your pillow is starting to recognise you."),
        new(5, "High Five", "Five nights. Give yourself one. Then go to bed."),
        new(7, "Week Warrior", "A full week! Science says the habit is forming. We say: nice."),
        new(10, "Double Digits", "Ten nights. \"One more game\" is losing its grip."),
        new(14, "Fortnight Knight", "Two weeks. Your eye bags have filed a complaint about being ignored."),
        new(21, "Habit Hacker", "21 nights. They say that is how long a habit takes. You did it."),
        new(30, "Moonwalker", "A whole month of real sleep. Who even are you?"),
        new(45, "Sandman's Apprentice", "45 nights. The Sandman wants to hire you."),
        new(60, "Dream Team", "Two months. Your future self sends a thank-you card."),
        new(75, "Pillow Pro", "75 nights. You and your pillow are officially a couple."),
        new(90, "Quarter Master", "Three months. A whole season of not seeing 3 AM."),
        new(120, "Night Owl, Retired", "120 nights. The owl has hung up its wings."),
        new(150, "Sleep Scholar", "150 nights. You could teach a class on this."),
        new(180, "Half-Year Hero", "Six months! Half a year of choosing sleep."),
        new(225, "Snooze Sage", "225 nights. Monks come to you for advice now."),
        new(270, "Nine-Month Ninja", "270 nights. Silent, rested, unstoppable."),
        new(300, "Three Hundred Club", "300 nights. This is just who you are now."),
        new(330, "Almost There", "330 nights. The finish line can see you."),
        new(365, "Sleep Legend", "ONE YEAR. A full trip around the sun, asleep on time. Absolute legend."),
    ];

    public static IReadOnlyList<Badge> Earned(int best) => All.Where(b => b.Nights <= best).ToList();
    public static Badge? Next(int best) => All.FirstOrDefault(b => b.Nights > best);
    public static Badge? At(int nights) => All.FirstOrDefault(b => b.Nights == nights);

    public static readonly IReadOnlyDictionary<int, string> MoodNames = new Dictionary<int, string>
    {
        [1] = "Rough", [2] = "Meh", [3] = "OK", [4] = "Good", [5] = "Great",
    };
}

public static class Streaks
{
    // A "clean night" is a guarded night where NightGuard didn't have to step in: you were off
    // before bedtime and stayed off. The streak counts clean nights in a row; free nights don't break it.

    /// <summary>How last night went. Null while a locked night is still running.</summary>
    public static NightReport? Report(NightGuardConfig cfg, DateTime now, DateTime since, IReadOnlySet<string> steppedIn)
    {
        if (ScheduleMath.CurrentNight(cfg, now) is not null) return null;
        int streak = 0, best = 0;
        var first = since.Date;
        if ((now.Date - first).TotalDays > 800) first = now.Date.AddDays(-800);
        for (var eve = first; eve < now.Date; eve = eve.AddDays(1))
        {
            var n = ScheduleMath.GetNight(cfg, eve, ignoreEndDate: true);
            if (n is null || n.End > now) continue;   // free night (doesn't break the streak)
            if (steppedIn.Contains(Key(eve))) streak = 0; else streak++;
            if (streak > best) best = streak;
        }
        var y = now.Date.AddDays(-1);
        var last = ScheduleMath.GetNight(cfg, y, ignoreEndDate: true);
        var kind = y < since.Date ? NightKind.New
                 : last is null ? NightKind.Free
                 : steppedIn.Contains(Key(y)) ? NightKind.SteppedIn
                 : NightKind.Clean;
        return new NightReport(kind, streak, best);
    }

    public static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
