using NightGuard.Core.Config;
using NightGuard.Core.Schedule;

namespace NightGuard.Core.Tests;

public class ScheduleTests
{
    // 2024-01-01 is a Monday
    private static readonly DateTime Monday = new(2024, 1, 1);

    private static NightGuardConfig Cfg(string bed = "23:00", string unlock = "06:00")
    {
        var c = new NightGuardConfig();
        foreach (var d in Defaults.Days) c.Schedule[d] = new NightSchedule { Enabled = true, Bedtime = bed, Unlock = unlock };
        return c;
    }

    [Fact]
    public void Evening_bedtime_starts_same_day()
    {
        var n = ScheduleMath.GetNight(Cfg(), Monday)!;
        Assert.Equal(Monday.AddHours(23), n.Start);
        Assert.Equal(Monday.AddDays(1).AddHours(6), n.End);
        Assert.Equal("Mon", n.Day);
    }

    [Fact]
    public void Bedtime_before_noon_means_after_midnight()
    {
        var n = ScheduleMath.GetNight(Cfg("00:30"), Monday)!;
        Assert.Equal(Monday.AddDays(1).AddMinutes(30), n.Start);
    }

    [Fact]
    public void Current_and_next_night()
    {
        var c = Cfg();
        Assert.NotNull(ScheduleMath.CurrentNight(c, Monday.AddDays(1).AddHours(2)));     // Tue 02:00, Monday night
        Assert.Null(ScheduleMath.CurrentNight(c, Monday.AddHours(12)));
        Assert.Equal(Monday.AddHours(23), ScheduleMath.NextNight(c, Monday.AddHours(12))!.Start);
    }

    [Fact]
    public void Free_night_is_not_locked()
    {
        var c = Cfg();
        c.Schedule["Fri"].Enabled = false;
        var friday = Monday.AddDays(4);
        Assert.Null(ScheduleMath.CurrentNight(c, friday.AddHours(23).AddMinutes(30)));
    }

    [Fact]
    public void End_date_expires_after_last_night()
    {
        var c = Cfg();
        c.EndDate = "2024-01-03";
        Assert.Equal(new DateTime(2024, 1, 4, 6, 0, 0), ScheduleMath.Expiry(c));
        Assert.False(ScheduleMath.IsExpired(c, new DateTime(2024, 1, 4, 5, 59, 0)));
        Assert.True(ScheduleMath.IsExpired(c, new DateTime(2024, 1, 4, 6, 0, 0)));
        Assert.Null(ScheduleMath.GetNight(c, new DateTime(2024, 1, 4)));
    }

    [Fact]
    public void Night_mode_starts_before_bedtime()
    {
        var c = Cfg();
        c.NightModeMinutes = 60;
        Assert.Null(ScheduleMath.NightModeReason(c, Monday.AddHours(21).AddMinutes(59)));
        Assert.NotNull(ScheduleMath.NightModeReason(c, Monday.AddHours(22)));
        Assert.NotNull(ScheduleMath.NightModeReason(c, Monday.AddDays(1).AddHours(5)));
        Assert.Null(ScheduleMath.NightModeReason(c, Monday.AddDays(1).AddHours(6)));
    }

    [Fact]
    public void Blocking_slots_cross_midnight()
    {
        var c = Cfg();
        c.Blocking.Enabled = true;
        c.Blocking.DuringBedtime = false;
        c.Blocking.Windows = [new BlockWindow { Days = ["Mon"], From = "22:00", To = "02:00" }];
        Assert.True(ScheduleMath.IsBlockActive(c, Monday.AddHours(22).AddMinutes(30)));
        Assert.True(ScheduleMath.IsBlockActive(c, Monday.AddDays(1).AddHours(1)));
        Assert.False(ScheduleMath.IsBlockActive(c, Monday.AddDays(1).AddHours(22).AddMinutes(30)));   // Tuesday not ticked
    }

    [Fact]
    public void Blocking_during_bedtime()
    {
        var c = Cfg();
        c.Blocking.Enabled = true;
        Assert.True(ScheduleMath.IsBlockActive(c, Monday.AddHours(23).AddMinutes(5)));
        Assert.False(ScheduleMath.IsBlockActive(c, Monday.AddHours(20)));
    }

    [Fact]
    public void Message_placeholders()
    {
        var c = Cfg();
        var n = ScheduleMath.GetNight(c, Monday)!;
        var t = ScheduleMath.FormatMessage(@"In {minutes} min we {action} at {bedtime}\nuntil {unlock}", c, n, 5);
        Assert.Equal("In 5 min we hibernate at 23:00\nuntil 06:00", t);
    }

    [Fact]
    public void Validation_rules()
    {
        Assert.Empty(ConfigValidator.Validate(Cfg(), TestPaths.Rules));
        var tooLong = Cfg("18:00", "11:00");   // 17 hours
        Assert.Contains(ConfigValidator.Validate(tooLong, TestPaths.Rules), e => e.Contains("16 hours"));
        var noGap = Cfg("20:00", "12:00");      // 16 h locked, 8 h free: fine
        Assert.Empty(ConfigValidator.Validate(noGap, TestPaths.Rules));
        var tooClose = Cfg("19:00", "11:00");   // 16 h locked, 8 h free... make it 5 h free
        tooClose.Schedule["Tue"].Bedtime = "16:00";
        Assert.Contains(ConfigValidator.Validate(tooClose, TestPaths.Rules), e => e.Contains("6 free hours"));
        var off = Cfg();
        foreach (var d in Defaults.Days) off.Schedule[d].Enabled = false;
        Assert.Contains(ConfigValidator.Validate(off, TestPaths.Rules), e => e.Contains("Every night is turned off"));
        var bad = Cfg();
        bad.CountdownSeconds = 5;
        bad.Blocking.Sites = ["microsoft.com", "not a site"];
        var errs = ConfigValidator.Validate(bad, TestPaths.Rules);
        Assert.Contains(errs, e => e.Contains("Countdown"));
        Assert.Contains(errs, e => e.Contains("Windows needs it"));
        Assert.Contains(errs, e => e.Contains("not a site name"));
    }

    [Fact]
    public void Protected_paths()
    {
        Assert.True(TestPaths.Rules.IsProtected(@"C:\Windows\System32\cmd.exe"));
        Assert.True(TestPaths.Rules.IsProtected(@"C:\Program Files"));
        Assert.True(TestPaths.Rules.IsProtected(@"D:\"));
        Assert.False(TestPaths.Rules.IsProtected(@"C:\Program Files\Steam"));
    }

    [Fact]
    public void Locked_hours_for_editor()
    {
        Assert.Equal(7, ScheduleMath.LockedHours("23:00", "06:00"));
        Assert.Equal(5.5, ScheduleMath.LockedHours("00:30", "06:00"));
        Assert.Null(ScheduleMath.LockedHours("07:00", "06:00"));
    }
}

internal static class TestPaths
{
    public static readonly NightGuard.Core.Blocking.PathRules Rules = new(
        [@"C:\Windows", @"C:\Program Files\Windows Defender", @"C:\ProgramData\Microsoft", @"C:\ProgramData\NightGuard"],
        [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData", @"C:\Users\me", @"C:\Users"]);
}
