using NightGuard.Core.Blocking;
using NightGuard.Core.Config;
using NightGuard.Core.Journal;
using NightGuard.Core.ScreenTime;
using NightGuard.Core.SelfLock;
using NightGuard.Core.Trusted;

namespace NightGuard.Core.Tests;

public class SelfLockTests
{
    private static NightGuardConfig Cfg()
    {
        var c = new NightGuardConfig();
        foreach (var d in Defaults.Days) c.Schedule[d] = new NightSchedule { Enabled = true, Bedtime = "23:00", Unlock = "06:00" };
        return c;
    }

    [Fact]
    public void Stricter_is_not_looser()
    {
        var o = Cfg();
        var n = Cfg();
        n.Schedule["Mon"].Bedtime = "22:00";
        n.Schedule["Tue"].Unlock = "07:00";
        n.Action = "Shutdown";
        n.CountdownSeconds = 30;
        Assert.Empty(SelfLockRules.Loosening(o, n));
    }

    [Fact]
    public void Looser_changes_are_listed()
    {
        var o = Cfg();
        var n = Cfg();
        n.Schedule["Mon"].Bedtime = "00:30";   // after midnight = later
        n.Schedule["Tue"].Enabled = false;
        n.Action = "Lock";
        n.EndDate = "2030-01-01";
        var l = SelfLockRules.Loosening(o, n);
        Assert.Contains("Mon night: later bedtime", l);
        Assert.Contains("Tue night is no longer locked", l);
        Assert.Contains(l, x => x.StartsWith("bedtime action"));
        Assert.Contains("new end date", l);
    }

    [Fact]
    public void Screen_time_and_blocking_loosening()
    {
        var o = Cfg();
        o.Blocking.Enabled = true;
        o.ScreenTime.Enabled = true;
        var n = o.Clone();
        n.Blocking.Sites.Remove("reddit.com");
        n.ScreenTime.GameMinutesWeekday = 90;
        n.ScreenTime.IgnoreProcesses.Add("cs2");
        var l = SelfLockRules.Loosening(o, n);
        Assert.Contains(l, x => x.Contains("reddit.com"));
        Assert.Contains("more game time on weekdays", l);
        Assert.Contains("more programs ignored by screen time", l);
    }

    [Fact]
    public void Unlock_cool_off()
    {
        var req = new DateTime(2024, 1, 1, 12, 0, 0);
        Assert.Equal(UnlockStateKind.Waiting, SelfLockRules.Unlock(req, req.AddDays(6)).State);
        Assert.Equal(UnlockStateKind.Open, SelfLockRules.Unlock(req, req.AddDays(7)).State);
        Assert.Equal(UnlockStateKind.None, SelfLockRules.Unlock(req, req.AddDays(10)).State);
        Assert.Equal(UnlockStateKind.None, SelfLockRules.Unlock(req, req.AddDays(-2)).State);   // clock went back
    }

    [Fact]
    public void End_date_blocks_uninstall()
    {
        var c = Cfg();
        c.EndDate = "2030-01-01";
        Assert.NotNull(SelfLockRules.UninstallBlock(c, new UnlockInfo(UnlockStateKind.Open, null, null, null)));
        c.EndDate = null;
        Assert.Null(SelfLockRules.UninstallBlock(c, new UnlockInfo(UnlockStateKind.Open, null, null, null)));
        Assert.NotNull(SelfLockRules.UninstallBlock(c, UnlockInfo.None));
        Assert.Null(SelfLockRules.UninstallBlock(null, UnlockInfo.None));
    }
}

public class TrustedCodeTests
{
    [Theory]
    [InlineData("1234567", "exactly 8")]
    [InlineData("00000000", "same digit")]
    [InlineData("12345678", "Counting")]
    [InlineData("98765432", "Counting")]
    [InlineData("12121212", "repeating")]
    [InlineData("11223344", "doubled")]
    [InlineData("47124712", "repeating")]
    [InlineData("19990415", "date")]
    public void Weak_codes_are_refused(string code, string why) =>
        Assert.Contains(why, TrustedCode.Problem(code)!, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Good_code_round_trips()
    {
        Assert.Null(TrustedCode.Problem("48201937"));
        var t = TrustedCode.Create("48201937", "Mum", DateTime.Now);
        Assert.True(TrustedCode.Matches(t, "48201937"));
        Assert.False(TrustedCode.Matches(t, "48201938"));
        Assert.DoesNotContain("48201937", t.Hash + t.Salt);
    }

    [Fact]
    public void Lockout_grows()
    {
        Assert.Null(TrustedCode.LockoutAfter(4));
        Assert.Equal(TimeSpan.FromMinutes(15), TrustedCode.LockoutAfter(5));
        Assert.Equal(TimeSpan.FromMinutes(30), TrustedCode.LockoutAfter(6));
        Assert.Equal(TimeSpan.FromMinutes(1440), TrustedCode.LockoutAfter(20));
    }
}

public class HostsTests
{
    [Fact]
    public void Adds_and_removes_section_without_touching_the_rest()
    {
        var original = "# comment\r\n127.0.0.1 localhost\r\n";
        var blocked = HostsFile.Apply(original, ["reddit.com", "www.reddit.com"])!;
        Assert.Contains("0.0.0.0 reddit.com", blocked);
        Assert.StartsWith(original, blocked);
        Assert.Null(HostsFile.Apply(blocked, ["reddit.com", "www.reddit.com"]));   // already right
        var removed = HostsFile.Apply(blocked, [])!;
        Assert.Equal("# comment\r\n127.0.0.1 localhost\r\n", removed + (removed.EndsWith("\r\n") ? "" : "\r\n"));
        Assert.Null(HostsFile.Apply(original, []));
    }

    [Fact]
    public void Missing_end_marker_only_removes_block_lines()
    {
        var text = $"a\r\n{HostsFile.BeginLine}\r\n0.0.0.0 x.com\r\n10.0.0.1 nas\r\n";
        var clean = HostsFile.RemoveSection(text);
        Assert.Contains("10.0.0.1 nas", clean);
        Assert.DoesNotContain("x.com", clean);
    }

    [Fact]
    public void Domain_variants_and_protection()
    {
        var c = new NightGuardConfig();
        c.Blocking.Sites = ["www.reddit.com", "live.com"];
        var d = HostsFile.Domains(c);
        Assert.Contains("reddit.com", d);
        Assert.Contains("m.reddit.com", d);
        Assert.DoesNotContain("live.com", d);
    }
}

public class StreakTests
{
    [Fact]
    public void Streak_counts_clean_nights_and_resets()
    {
        var c = new NightGuardConfig();
        foreach (var d in Defaults.Days) c.Schedule[d] = new NightSchedule { Enabled = true, Bedtime = "23:00", Unlock = "06:00" };
        var since = new DateTime(2024, 1, 1);
        var now = new DateTime(2024, 1, 8, 9, 0, 0);
        var clean = Streaks.Report(c, now, since, new HashSet<string>())!;
        Assert.Equal(NightKind.Clean, clean.Kind);
        Assert.Equal(7, clean.Streak);
        var stepped = Streaks.Report(c, now, since, new HashSet<string> { "2024-01-05" })!;
        Assert.Equal(2, stepped.Streak);
        Assert.Equal(4, stepped.Best);
        Assert.Null(Streaks.Report(c, new DateTime(2024, 1, 8, 2, 0, 0), since, new HashSet<string>()));
        Assert.Equal(NightKind.New, Streaks.Report(c, new DateTime(2024, 1, 1, 9, 0, 0), since, new HashSet<string>())!.Kind);
    }

    [Fact]
    public void Badges()
    {
        Assert.Equal("Week Warrior", Journal.Badges.At(7)!.Name);
        Assert.Equal(4, Journal.Badges.Earned(7).Count);
        Assert.Equal(10, Journal.Badges.Next(7)!.Nights);
        Assert.Null(Journal.Badges.Next(365));
    }
}

public class ScreenTimeTests
{
    private static ScreenTimeSettings St() => new() { Enabled = true, GameMinutesWeekday = 60, ScreenMinutesWeekday = 120, LimitAction = "Lock" };

    [Fact]
    public void Counts_and_warns_then_closes_games()
    {
        var st = St();
        var u = new UsageState();
        var now = new DateTime(2024, 1, 1, 18, 0, 0);   // Monday
        var game = new ProcInfo(42, "cs2", @"D:\Games\cs2.exe", 1);
        u.Day = "2024-01-01";
        u.GameMinutes = 44;
        u.LastTick = now.AddMinutes(-1);
        var r = ScreenTimeEngine.Tick(st, u, now, true, [game], TestPaths.Rules, @"C:\Windows");
        Assert.Equal(45, u.GameMinutes);
        Assert.Contains(r.Notices, n => n.Message.Contains("game time left"));
        u.GameMinutes = 59.5;
        r = ScreenTimeEngine.Tick(st, u, now.AddMinutes(1), true, [game], TestPaths.Rules, @"C:\Windows");
        Assert.Contains(r.Kill, p => p.Id == 42);
        Assert.Contains(r.Notices, n => n.Message.Contains("used up"));
    }

    [Fact]
    public void Day_resets_at_reset_hour()
    {
        var st = St();
        var u = new UsageState { Day = "2024-01-01", ScreenMinutes = 100 };
        ScreenTimeEngine.Tick(st, u, new DateTime(2024, 1, 2, 3, 0, 0), true, [], TestPaths.Rules, @"C:\Windows");
        Assert.Equal("2024-01-01", u.Day);   // 3 AM still counts as Monday
        var r = ScreenTimeEngine.Tick(st, u, new DateTime(2024, 1, 2, 4, 0, 0), true, [], TestPaths.Rules, @"C:\Windows");
        Assert.True(r.DayRolledOver);
        Assert.Equal("2024-01-02", u.Day);
    }

    [Fact]
    public void Screen_limit_locks()
    {
        var st = St();
        var u = new UsageState { Day = "2024-01-01", ScreenMinutes = 130 };
        var r = ScreenTimeEngine.Tick(st, u, new DateTime(2024, 1, 1, 18, 0, 0), true, [], TestPaths.Rules, @"C:\Windows");
        Assert.Contains(r.Notices, n => n.Lock);
    }

    [Fact]
    public void Windows_and_ignored_programs_are_never_games()
    {
        var st = St();
        st.GameFolders = [@"D:\Games"];
        Assert.False(ScreenTimeEngine.IsGame(st, new ProcInfo(1, "explorer", @"C:\Windows\explorer.exe", 1), TestPaths.Rules, @"C:\Windows"));
        Assert.False(ScreenTimeEngine.IsGame(st, new ProcInfo(1, "steam", @"D:\Games\steam.exe", 1), TestPaths.Rules, @"C:\Windows"));
        Assert.True(ScreenTimeEngine.IsGame(st, new ProcInfo(1, "mygame", @"D:\Games\X\mygame.exe", 1), TestPaths.Rules, @"C:\Windows"));
        Assert.False(ScreenTimeEngine.IsGame(st, new ProcInfo(1, "mygame", @"D:\Games\X\mygame.exe", 0), TestPaths.Rules, @"C:\Windows"));
    }
}

public class ConfigFileTests
{
    [Fact]
    public void Reads_powershell_version3_file()
    {
        const string json = """
        {
          "Version": 3,
          "Schedule": { "Mon": { "Enabled": true, "Bedtime": "23:30", "Unlock": "06:00" } },
          "Warnings": [ { "MinutesBefore": 5, "Sound": true, "Message": "x" }, { "MinutesBefore": 30, "Sound": false, "Message": "y" } ],
          "Action": "Shutdown", "CountdownSeconds": 60, "RecheckMinutes": 1, "NightModeMinutes": 60, "EndDate": null,
          "MorningCheckIn": true, "Why": " sleep ", "ShortcutStartMenu": true, "ShortcutDesktop": false,
          "Blocking": { "Enabled": true, "DuringBedtime": true, "Sites": "Reddit.com", "Apps": [], "Windows": [ { "Days": "Mon", "From": "09:00", "To": "17:00" } ] },
          "ScreenTime": { "Enabled": false, "ResetHour": 4, "WarnAtMinutesLeft": 15, "GameProcesses": ["cs2.exe"] }
        }
        """;
        var c = ConfigSerializer.Read(json);
        Assert.Equal("23:30", c.Schedule["Mon"].Bedtime);
        Assert.False(c.Schedule["Tue"].Enabled);
        Assert.Equal(30, c.Warnings[0].MinutesBefore);   // sorted, longest first
        Assert.Equal(["reddit.com"], c.Blocking.Sites);
        Assert.Equal(["Mon"], c.Blocking.Windows[0].Days);
        Assert.Equal([15], c.ScreenTime.WarnAtMinutesLeft);
        Assert.Equal(["cs2"], c.ScreenTime.GameProcesses);
        Assert.Equal("sleep", c.Why);
        // Round trip
        var again = ConfigSerializer.Read(ConfigSerializer.Write(c));
        Assert.Equal(ConfigSerializer.Write(c), ConfigSerializer.Write(again));
    }

    [Fact]
    public void Reads_version1_file()
    {
        var c = ConfigSerializer.Read("""{ "ShutdownTime": "23:00", "UnlockTime": "06:30", "WarnTimes": ["22:30", "22:55"], "EndDate": null }""");
        Assert.All(Defaults.Days, d => Assert.Equal("23:00", c.Schedule[d].Bedtime));
        Assert.Equal([30, 5], c.Warnings.Select(w => w.MinutesBefore));
    }

    [Fact]
    public void Default_config_is_valid() => Assert.Empty(ConfigValidator.Validate(new NightGuardConfig(), TestPaths.Rules));
}
