using System.Globalization;
using NightGuard.Core.Blocking;
using NightGuard.Core.Config;

namespace NightGuard.Core.ScreenTime;

/// <summary>usage.json: today's counted minutes and which notices were already sent.</summary>
public sealed class UsageState
{
    public string Day { get; set; } = "";
    public double GameMinutes { get; set; }
    public double ScreenMinutes { get; set; }
    public DateTime? LastTick { get; set; }
    public List<string> Sent { get; set; } = [];
    public bool GameLimitNotified { get; set; }
    public DateTime? LastGameKillNotice { get; set; }
    public DateTime? LastScreenAction { get; set; }
    public DateTime? ShutdownIssuedAt { get; set; }
}

public sealed record ProcInfo(int Id, string Name, string? Path, int SessionId);

public sealed record Notice(string Message, bool Lock);

public sealed class TickResult
{
    public List<Notice> Notices { get; } = [];
    public List<ProcInfo> Kill { get; } = [];
    public int? ShutdownInMinutes { get; set; }
    public List<string> Log { get; } = [];
    public bool DayRolledOver { get; set; }
}

/// <summary>
/// Game and screen time, one tick (about a minute) at a time. Pure logic: the service gathers the
/// facts (is the screen in use, which processes run) and carries out what comes back.
/// The day starts at ResetHour (default 4 AM), so late-night play counts toward the evening before.
/// </summary>
public static class ScreenTimeEngine
{
    public static DateTime UsageDay(ScreenTimeSettings st, DateTime now) => now.AddHours(-st.ResetHour).Date;

    public static (int Game, int Screen) Limits(ScreenTimeSettings st, DateTime day)
    {
        var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        return weekend ? (st.GameMinutesWeekend, st.ScreenMinutesWeekend) : (st.GameMinutesWeekday, st.ScreenMinutesWeekday);
    }

    public static string FormatMinutes(double minutes)
    {
        var m = (int)Math.Max(0, Math.Round(minutes));
        return $"{m / 60}h {m % 60:00}m";
    }

    public static string FormatLeft(double minutes)
    {
        var m = (int)Math.Max(0, Math.Ceiling(minutes));
        return m >= 60 ? $"{m / 60}h {m % 60:00}m" : $"{m} min";
    }

    /// <summary>Is this process a game, according to the settings?</summary>
    public static bool IsGame(ScreenTimeSettings st, ProcInfo p, PathRules paths, string windowsDir)
    {
        if (p.SessionId == 0 || string.IsNullOrEmpty(p.Name)) return false;
        var name = ConfigSerializer.StripExe(p.Name).ToLowerInvariant();
        if (Defaults.NeverKill.Contains(name)) return false;
        if (st.IgnoreProcesses.Any(i => string.Equals(i, name, StringComparison.OrdinalIgnoreCase))) return false;
        if (st.GameProcesses.Any(g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase))) return true;
        if (string.IsNullOrEmpty(p.Path)) return false;
        if (!string.IsNullOrEmpty(windowsDir) && p.Path.StartsWith(windowsDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var f in st.GameFolders)
        {
            if (paths.IsProtected(f)) continue;
            if (p.Path.StartsWith(f.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static TickResult Tick(ScreenTimeSettings st, UsageState u, DateTime now, bool screenActive,
                                  IReadOnlyList<ProcInfo> procs, PathRules paths, string windowsDir)
    {
        var r = new TickResult();
        var usageDay = UsageDay(st, now);
        var dayKey = usageDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (gameLimit, screenLimit) = Limits(st, usageDay);

        // ---- Roll over at the day boundary ----
        if (u.Day != dayKey)
        {
            if (!string.IsNullOrEmpty(u.Day))
                r.Log.Add($"screen time: {u.Day} finished, games {FormatMinutes(u.GameMinutes)}, screen {FormatMinutes(u.ScreenMinutes)}");
            var lastTick = u.LastTick;
            Reset(u, dayKey);
            u.LastTick = lastTick;
            r.DayRolledOver = true;
        }

        // Time since the last tick, capped so sleep / power-off / missed ticks are not counted
        var elapsed = 1.0;
        if (u.LastTick is { } last)
        {
            elapsed = (now - last).TotalMinutes;
            if (elapsed < 0) elapsed = 0;
            if (elapsed > 2) elapsed = 1;
        }
        u.LastTick = now;

        var games = procs.Where(p => IsGame(st, p, paths, windowsDir)).ToList();

        // ---- Count time ----
        if (screenActive)
        {
            u.ScreenMinutes += elapsed;
            if (games.Count > 0) u.GameMinutes += elapsed;
        }
        var gameLeft = gameLimit - u.GameMinutes;
        var screenLeft = screenLimit - u.ScreenMinutes;
        var warnAt = st.WarnAtMinutesLeft.OrderByDescending(x => x).ToList();

        // ---- Warnings (one per crossed threshold) ----
        bool ShouldWarn(string kind, double left)
        {
            if (left <= 0) return false;
            var crossed = warnAt.Where(t => left <= t && !u.Sent.Contains($"{kind}-{t}")).ToList();
            if (crossed.Count == 0) return false;
            foreach (var t in crossed) u.Sent.Add($"{kind}-{t}");
            return true;
        }
        var action = st.LimitAction;
        if (gameLimit > 0 && games.Count > 0 && screenActive && ShouldWarn("game", gameLeft))
            r.Notices.Add(new Notice($"About {FormatLeft(gameLeft)} of game time left today. Save your game.", false));
        if (screenLimit > 0 && screenActive && ShouldWarn("screen", screenLeft))
        {
            var what = action switch { "Shutdown" => "the PC will shut down", "Warn" => "you will be reminded to stop", _ => "the PC will lock" };
            r.Notices.Add(new Notice($"About {FormatLeft(screenLeft)} of screen time left today. After that {what}.", false));
        }

        // ---- Game budget used up: close games (and launchers) ----
        if (gameLimit > 0 && gameLeft <= 0)
        {
            var kill = new List<ProcInfo>(games);
            if (st.CloseLaunchers)
            {
                kill.AddRange(procs.Where(p => p.SessionId != 0
                    && st.LauncherProcesses.Any(l => string.Equals(l, ConfigSerializer.StripExe(p.Name), StringComparison.OrdinalIgnoreCase))
                    && !kill.Any(k => k.Id == p.Id)));
            }
            kill.RemoveAll(p => Defaults.NeverKill.Contains(ConfigSerializer.StripExe(p.Name).ToLowerInvariant()));
            r.Kill.AddRange(kill);
            if (!u.GameLimitNotified)
            {
                u.GameLimitNotified = true;
                u.LastGameKillNotice = now;
                r.Notices.Add(new Notice($"Game time for today is used up. Games will be closed until {st.ResetHour}:00.", false));
            }
            else if (kill.Count > 0 && (u.LastGameKillNotice is not { } lk || (now - lk).TotalMinutes >= 3))
            {
                u.LastGameKillNotice = now;
                r.Notices.Add(new Notice("Game time for today is used up, so that game was closed.", false));
            }
        }

        // ---- Screen budget used up ----
        if (screenLimit > 0 && screenLeft <= 0 && screenActive)
        {
            switch (action)
            {
                case "Warn":
                    if (u.LastScreenAction is not { } la || (now - la).TotalMinutes >= 10)
                    {
                        u.LastScreenAction = now;
                        r.Notices.Add(new Notice("Screen time for today is used up. Time to stop.", false));
                    }
                    break;
                case "Shutdown":
                    // Re-issue if an earlier shutdown didn't happen and the PC is still in use
                    if (u.ShutdownIssuedAt is not { } issued || (now - issued).TotalMinutes >= st.ShutdownGraceMinutes + 2)
                    {
                        u.ShutdownIssuedAt = now;
                        r.ShutdownInMinutes = st.ShutdownGraceMinutes;
                        r.Notices.Add(new Notice($"Screen time for today is used up. The PC shuts down in {st.ShutdownGraceMinutes} minute(s). Save your work.", false));
                    }
                    break;
                default:
                    // Lock again every tick while someone is using the screen
                    u.LastScreenAction = now;
                    r.Notices.Add(new Notice($"Screen time for today is used up. Locking the PC until {st.ResetHour}:00.", true));
                    break;
            }
        }
        return r;
    }

    public static void Reset(UsageState u, string dayKey)
    {
        u.Day = dayKey;
        u.GameMinutes = 0;
        u.ScreenMinutes = 0;
        u.LastTick = null;
        u.Sent = [];
        u.GameLimitNotified = false;
        u.LastGameKillNotice = null;
        u.LastScreenAction = null;
        u.ShutdownIssuedAt = null;
    }

    public static string UsageText(ScreenTimeSettings st, UsageState? u, DateTime now)
    {
        if (!st.Enabled) return "Screen time limits are off.";
        var day = UsageDay(st, now);
        var (gl, sl) = Limits(st, day);
        double game = 0, screen = 0;
        if (u is not null && u.Day == day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) { game = u.GameMinutes; screen = u.ScreenMinutes; }
        static string Line(string name, double used, int limit) => limit <= 0
            ? $"{name}: {FormatMinutes(used)} used (no limit)"
            : $"{name}: {FormatMinutes(used)} of {FormatMinutes(limit)} used, {FormatMinutes(limit - used)} left";
        return $"Today ({day:ddd d MMM}, resets at {st.ResetHour}:00)\n{Line("Games", game, gl)}\n{Line("Screen", screen, sl)}";
    }
}
