using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NightGuard.Core.Blocking;
using NightGuard.Core.Config;
using NightGuard.Core.Ipc;
using NightGuard.Core.Journal;
using NightGuard.Core.Schedule;

namespace NightGuard.App;

// Moving settings between the pages and the config, and the live checks.
public partial class MainWindow
{
    // ================================================================ form <-> settings
    private static string Compose(Defaults.WarningPreset p, string custom)
    {
        var extra = custom.Trim().Replace("\r", "", StringComparison.Ordinal).Replace("\n", @"\n", StringComparison.Ordinal);
        return extra.Length == 0 ? p.Message : p.Message + Defaults.CustomSeparator + extra;
    }

    private static string CustomPart(Defaults.WarningPreset p, string message) =>
        message.StartsWith(p.Message + Defaults.CustomSeparator, StringComparison.Ordinal)
            ? message[(p.Message.Length + Defaults.CustomSeparator.Length)..].Replace(@"\n", " ", StringComparison.Ordinal)
            : "";

    private void LoadForm(NightGuardConfig cfg)
    {
        _loading = true;
        for (var i = 0; i < 7; i++)
        {
            var s = cfg.Night(Defaults.Days[i]);
            _nights[i].On.IsChecked = s.Enabled;
            _nights[i].Bed.Value = s.Bedtime;
            _nights[i].Unlock.Value = s.Unlock;
        }

        // Warnings: each one goes to the card that matches its timing (older setups could have up to 6)
        foreach (var w in _warns)
        {
            w.On.IsChecked = false;
            w.Minutes.Value = w.Preset.DefaultMinutes;
            w.Sound.IsChecked = true;
            w.Custom.Text = "";
        }
        var used = new HashSet<int>();
        foreach (var setting in cfg.Warnings)
        {
            var idx = Defaults.PresetIndex(setting.MinutesBefore);
            if (!used.Add(idx)) continue;
            var w = _warns[idx];
            w.On.IsChecked = true;
            w.Minutes.Value = setting.MinutesBefore;
            w.Sound.IsChecked = setting.Sound;
            w.Custom.Text = CustomPart(w.Preset, setting.Message);
        }

        SelectAction(cfg.Action);
        CountdownPills.Value = cfg.CountdownSeconds;
        RecheckPills.Value = cfg.RecheckMinutes;
        NightModePills.Value = cfg.NightModeMinutes;
        _savedEndDate = cfg.EndDate;
        LegacyEndCard.Visibility = cfg.EndDate is null ? Visibility.Collapsed : Visibility.Visible;
        LegacyEndText.Text = ScheduleMath.EndDate(cfg) is { } end
            ? $"📅 An earlier version set an end date: the last locked night is {end:dddd d MMMM yyyy}, then NightGuard removes itself. It stays as it is."
            : "";

        WhyBox.Text = cfg.Why;
        PlanBox.Text = cfg.Plan;
        CheckInCheck.IsChecked = cfg.MorningCheckIn;
        DesktopCheck.IsChecked = cfg.ShortcutDesktop;

        _savedCommit = cfg.CommitNights;
        _savedCommitStart = cfg.CommitStart;
        _commitChoice = cfg.CommitNights;
        _signed = cfg.CommitNights is not null;
        _recommit = false;
        _customChosen = cfg.CommitNights is > 0 and var n && !Commitment.Choices.Contains(n);
        if (_customChosen) _commitCustom.Value = cfg.CommitNights!.Value;
        UpdateCommitUi();

        var b = cfg.Blocking;
        BlockCheck.IsChecked = b.Enabled;
        BlockBedtimeCheck.IsChecked = b.DuringBedtime;
        SitesEditor.SetItems(b.Sites);
        AppsEditor.SetItems(b.Apps);
        for (var i = 0; i < 4; i++)
        {
            var row = _slots[i];
            if (i < b.Windows.Count)
            {
                var w = b.Windows[i];
                row.On.IsChecked = true;
                for (var d = 0; d < 7; d++) row.Days[d].IsChecked = w.Days.Contains(Defaults.Days[d]);
                row.From.Value = w.From;
                row.To.Value = w.To;
            }
            else row.On.IsChecked = false;
        }

        var st = cfg.ScreenTime;
        // Each budget has its own switch. Off is stored as 0 minutes; the boxes then show the usual defaults.
        var gameOn = st.Enabled && (st.GameMinutesWeekday > 0 || st.GameMinutesWeekend > 0);
        var screenOn = st.Enabled && (st.ScreenMinutesWeekday > 0 || st.ScreenMinutesWeekend > 0);
        GameLimitCheck.IsChecked = gameOn;
        ScreenLimitCheck.IsChecked = screenOn;
        GameWd.Value = gameOn ? st.GameMinutesWeekday : 60;
        GameWe.Value = gameOn ? st.GameMinutesWeekend : 120;
        ScreenWd.Value = screenOn ? st.ScreenMinutesWeekday : 240;
        ScreenWe.Value = screenOn ? st.ScreenMinutesWeekend : 360;
        ResetHour.Value = st.ResetHour;
        GraceBox.Value = st.ShutdownGraceMinutes;
        LimitPills.Value = Math.Max(0, Array.IndexOf(Defaults.ScreenActions, st.LimitAction));
        WarnAtPills.Values = st.WarnAtMinutesLeft.Where(m => m > 0).ToList();
        LaunchersCheck.IsChecked = st.CloseLaunchers;
        GameFoldersEditor.SetItems(st.GameFolders);
        GameProcsEditor.SetItems(st.GameProcesses);
        IgnoreProcsEditor.SetItems(st.IgnoreProcesses);
        LauncherProcsEditor.SetItems(st.LauncherProcesses);
        UsageText.Text = _status is { Configured: true, ScreenTimeEnabled: true } ? _status.UsageText : "";
        UsageText.Visibility = UsageText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _loading = false;
    }


    private NightGuardConfig ReadForm()
    {
        var cfg = new NightGuardConfig();
        for (var i = 0; i < 7; i++)
            cfg.Schedule[Defaults.Days[i]] = new NightSchedule { Enabled = _nights[i].On.IsChecked == true, Bedtime = _nights[i].Bed.Value, Unlock = _nights[i].Unlock.Value };
        cfg.Warnings = _warns.Where(w => w.On.IsChecked == true)
            .Select(w => new WarningSetting { MinutesBefore = w.Minutes.Value, Sound = w.Sound.IsChecked == true, Message = Compose(w.Preset, w.Custom.Text) }).ToList();
        cfg.Action = _action;
        cfg.CountdownSeconds = CountdownPills.Value;
        cfg.RecheckMinutes = RecheckPills.Value;
        cfg.NightModeMinutes = NightModePills.Value;
        cfg.EndDate = _savedEndDate;   // no longer set here; an existing one is kept as it is
        cfg.Why = WhyBox.Text.Trim();
        cfg.Plan = PlanBox.Text.Trim();
        cfg.MorningCheckIn = CheckInCheck.IsChecked == true;
        if (_signed && _commitChoice is { } c)
        {
            var keep = !_recommit && c == _savedCommit && _savedCommitStart is not null;
            cfg.CommitNights = c;
            cfg.CommitStart = keep ? _savedCommitStart : DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else
        {
            cfg.CommitNights = _savedCommit;
            cfg.CommitStart = _savedCommitStart;
        }
        cfg.ShortcutDesktop = DesktopCheck.IsChecked == true;
        cfg.Blocking = new BlockingSettings
        {
            Enabled = BlockCheck.IsChecked == true,
            DuringBedtime = BlockBedtimeCheck.IsChecked == true,
            Sites = SitesEditor.Items.Select(s => s.ToLowerInvariant()).ToList(),
            Apps = AppsEditor.Items.ToList(),
            Windows = _slots.Where(s => s.On.IsChecked == true).Select(s => new BlockWindow
            {
                Days = Enumerable.Range(0, 7).Where(d => s.Days[d].IsChecked == true).Select(d => Defaults.Days[d]).ToList(),
                From = s.From.Value, To = s.To.Value,
            }).ToList(),
        };
        cfg.ScreenTime = new ScreenTimeSettings
        {
            Enabled = GameLimitCheck.IsChecked == true || ScreenLimitCheck.IsChecked == true,
            ResetHour = ResetHour.Value,
            GameMinutesWeekday = GameLimitCheck.IsChecked == true ? GameWd.Value : 0,
            GameMinutesWeekend = GameLimitCheck.IsChecked == true ? GameWe.Value : 0,
            ScreenMinutesWeekday = ScreenLimitCheck.IsChecked == true ? ScreenWd.Value : 0,
            ScreenMinutesWeekend = ScreenLimitCheck.IsChecked == true ? ScreenWe.Value : 0,
            WarnAtMinutesLeft = WarnAtPills.Values,
            LimitAction = Defaults.ScreenActions[Math.Clamp(LimitPills.Value, 0, Defaults.ScreenActions.Length - 1)],
            ShutdownGraceMinutes = GraceBox.Value,
            CloseLaunchers = LaunchersCheck.IsChecked == true,
            GameFolders = GameFoldersEditor.Items.ToList(),
            GameProcesses = GameProcsEditor.Items.ToList(),
            IgnoreProcesses = IgnoreProcsEditor.Items.ToList(),
            LauncherProcesses = LauncherProcsEditor.Items.ToList(),
        };
        return ConfigSerializer.Normalize(cfg);
    }

    // ================================================================ live checks
    /// <summary>Hours from 6 PM on the evening a night starts (bedtime and unlock), for the little timeline bars.</summary>
    private static (double Start, double End) Hours(string bed, string unlock)
    {
        var b = ScheduleMath.ParseTime(bed).TotalHours;
        var u = ScheduleMath.ParseTime(unlock).TotalHours;
        var start = b < 12 ? b + 24 : b;
        return (start - 18, u + 24 - 18);
    }

    private static void SetBar(NightCard r, bool on)
    {
        var (s, e) = Hours(r.Bed.Value, r.Unlock.Value);
        s = Math.Clamp(s, 0, 18);
        e = Math.Clamp(e, s, 18);
        r.Before.Width = new GridLength(Math.Max(0.001, s), GridUnitType.Star);
        r.Locked.Width = new GridLength(Math.Max(0.001, e - s), GridUnitType.Star);
        r.After.Width = new GridLength(Math.Max(0.001, 18 - e), GridUnitType.Star);
        r.Fill.Opacity = on ? 1 : 0.2;
    }

    private void OnFormChanged()
    {
        if (_loading || _pages is null) return;
        var form = ReadForm();
        // What's looser than what NightGuard runs with now (same rules the service applies when saving)
        var looser = _savedCfg is null ? [] : NightGuard.Core.SelfLock.SelfLockRules.Loosening(_savedCfg, form);
        var canLoosen = TrustedOn || _status?.UnlockState == "Open";
        for (var i = 0; i < 7; i++)
        {
            var r = _nights[i];
            var on = r.On.IsChecked == true;
            r.Bed.IsEnabled = r.Unlock.IsEnabled = on;
            SetBar(r, on);
            if (!on) { r.Note.Text = "\U0001F389 Free night"; r.Note.ClearValue(TextBlock.ForegroundProperty); continue; }
            var hours = ScheduleMath.LockedHours(r.Bed.Value, r.Unlock.Value);
            var text = hours is { } h ? $"\U0001F634 Locked {h:0.#} hours" : "⚠️ Unlock must be after bedtime";
            if (ScheduleMath.ParseTime(r.Bed.Value).TotalHours < 12) text += $" (early {Defaults.DayNames[(i + 1) % 7][..3]})";
            var day = Defaults.Days[i];
            var nightLooser = looser.Any(l => l.StartsWith(day + " night", StringComparison.Ordinal));
            if (nightLooser) text += canLoosen ? " · 🔓 looser than now" : " · 🔒 looser than now (needs the cool-off)";
            r.Note.Text = text;
            if (hours is null or > 16) r.Note.Foreground = Res("NgRed");
            else if (nightLooser) r.Note.Foreground = Res("NgOrange");
            else r.Note.ClearValue(TextBlock.ForegroundProperty);
        }
        // Turning a locked night off is looser too
        for (var i = 0; i < 7; i++)
        {
            var r = _nights[i];
            if (r.On.IsChecked == true || !looser.Contains($"{Defaults.Days[i]} night is no longer locked")) continue;
            r.Note.Text = canLoosen ? "🔓 Free night (looser than now)" : "🔒 Free night: looser than now (needs the cool-off)";
            r.Note.Foreground = Res("NgOrange");
        }
        foreach (var w in _warns) w.Body.IsEnabled = w.On.IsChecked == true;
        GameBody.IsEnabled = GameLimitCheck.IsChecked == true;
        ScreenLimitBody.IsEnabled = ScreenLimitCheck.IsChecked == true;
        SharedScreenCard.IsEnabled = GameLimitCheck.IsChecked == true || ScreenLimitCheck.IsChecked == true;
        GracePanel.Visibility = LimitPills.Value == 1 ? Visibility.Visible : Visibility.Collapsed;
        BlockBody.IsEnabled = BlockCheck.IsChecked == true;
        BlockBedtimeCheck.IsEnabled = BlockCheck.IsChecked == true;
        foreach (var s in _slots) s.Body.IsEnabled = s.On.IsChecked == true;
        if (_pages[PageIndex] != PageReview && ConfirmCheck.IsChecked == true) ConfirmCheck.IsChecked = false;

        var errs = ConfigValidator.Validate(form);
        if (errs.Count > 0)
        {
            BottomHint.Foreground = Res("NgRed");
            BottomHint.Text = "Fix before saving: " + errs[0] + (errs.Count > 1 ? $" (+{errs.Count - 1} more)" : "");
        }
        else if (looser.Count > 0)
        {
            BottomHint.Foreground = Res("NgOrange");
            BottomHint.Text = (canLoosen ? "🔓 Looser than now (allowed: " + (TrustedOn ? "trusted-person code" : "your unlock is open") + "): " : "🚧 Looser than now, so it needs the 7-day cool-off: ")
                + looser[0] + (looser.Count > 1 ? $" (+{looser.Count - 1} more)" : "");
        }
        else if (_savedCfg is not null)
        {
            BottomHint.Foreground = Res("NgGreen");
            BottomHint.Text = "\u2705 Same or stricter than now: this saves right away. You can always make it harder.";
        }
        else
        {
            BottomHint.Foreground = Res("NgMuted");
            BottomHint.Text = Configured
                ? "Stricter changes save right away; looser ones need the 7-day cool-off."
                : "Nothing starts until the last step. Jump to any step on the left.";
        }
    }

    private void Section_Changed(object sender, RoutedEventArgs e) => OnFormChanged();

    private void WhyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (WhyCount is null) return;
        WhyCount.Text = $"{WhyBox.Text.Length} / 300";
    }
}
