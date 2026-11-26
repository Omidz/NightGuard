using System.Globalization;

namespace NightGuard.Core.Journal;

/// <summary>Where you are in the nights you committed to. A goal to celebrate, not a lock.</summary>
public sealed record CommitProgress(int Night, int Total, bool Forever, bool Done, int NightsPassed);

public static class Commitment
{
    /// <summary>The choices on the "I commit to" card. 66 days is the average time a habit takes to stick (Lally et al., 2010).</summary>
    public static readonly int[] Choices = [7, 30, 66];

    public static DateTime? StartDate(string? start) =>
        start is { } s && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Null if there's no commitment yet.</summary>
    public static CommitProgress? Progress(int? nights, string? start, DateTime today)
    {
        if (nights is not { } total || StartDate(start) is not { } from) return null;
        var passed = Math.Max(0, (today.Date - from.Date).Days);
        if (total == 0) return new CommitProgress(passed + 1, 0, Forever: true, Done: false, passed);
        return new CommitProgress(Math.Min(passed + 1, total), total, Forever: false, Done: passed >= total, passed);
    }

    public static string Describe(int? nights) => nights switch
    {
        null => "no commitment yet",
        0 => "forever",
        1 => "1 night",
        { } n => $"{n} nights",
    };
}
