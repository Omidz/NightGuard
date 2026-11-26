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

// The review page, saving (with the self-lock and trusted code), and uninstalling.
public partial class MainWindow
{
    // ================================================================ review page
    private Border ReviewCard(string emoji, string title, int page, params UIElement[] body)
    {
        var sp = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var edit = new Button { Content = "Edit", VerticalAlignment = VerticalAlignment.Center };
        edit.SetResourceReference(StyleProperty, "LinkButton");
        edit.Click += (_, _) => Go(page);
        DockPanel.SetDock(edit, Dock.Right);
        head.Children.Add(edit);
        var badge = Badge(emoji, 40);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var t = Text(title, "H2");
        t.Margin = new Thickness(0);
        t.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(t);
        sp.Children.Add(head);
        foreach (var b in body) sp.Children.Add(b);
        return Card(sp, 318);
    }

    private TextBlock Line(string text, bool strong = false, string? brush = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = strong ? 15 : 13.5, Margin = new Thickness(0, 0, 0, 5), Foreground = Res(brush ?? (strong ? "NgText" : "NgMuted")) };
        if (strong) t.FontWeight = FontWeights.SemiBold;
        return t;
    }

    private UIElement Week(NightGuardConfig cfg)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 7; i++)
        {
            var s = cfg.Schedule[Defaults.Days[i]];
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var day = new TextBlock { Text = Defaults.Days[i], FontSize = 13, Foreground = Res("NgMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(day, i);
            g.Children.Add(day);
            var on = s.Enabled && ScheduleMath.IsTime(s.Bedtime) && ScheduleMath.IsTime(s.Unlock);
            var (a, b) = on ? Hours(s.Bedtime, s.Unlock) : (0, 0);
            a = Math.Clamp(a, 0, 18); b = Math.Clamp(b, a, 18);
            var bar = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, a), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, b - a), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 18 - b), GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(4), Background = Res("NgSurface2") };
            Grid.SetColumnSpan(track, 3);
            bar.Children.Add(track);
            if (on)
            {
                var fill = new Border { CornerRadius = new CornerRadius(4), Background = Res("NgMoon") };
                Grid.SetColumn(fill, 1);
                bar.Children.Add(fill);
            }
            Grid.SetRow(bar, i);
            Grid.SetColumn(bar, 1);
            g.Children.Add(bar);
            var t = new TextBlock
            {
                Text = on ? $"{TimeText.Format(s.Bedtime)} to {TimeText.Format(s.Unlock)}" : "free 🎉", FontSize = 12,
                Foreground = Res(on ? "NgText" : "NgMuted"), VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(t, i);
            Grid.SetColumn(t, 2);
            g.Children.Add(t);
        }
        return g;
    }

    private async Task UpdateReviewAsync()
    {
        ConfirmCheck.IsChecked = false;
        var cfg = ReadForm();
        var errs = ConfigValidator.Validate(cfg);
        ErrorCard.Visibility = errs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = string.Join("\n", errs.Select(e => "•  " + e));
        LooseCard.Visibility = Visibility.Collapsed;

        ReviewCards.Children.Clear();

        // Your why
        var why = new List<UIElement>();
        if (cfg.Why.Length > 0)
        {
            var q = new TextBlock { Text = $"“{cfg.Why}”", Margin = new Thickness(0, 0, 0, 8) };
            q.SetResourceReference(StyleProperty, "SerifQuote");
            q.FontSize = 18;
            q.LineHeight = 25;
            why.Add(q);
        }
        else why.Add(Line("No why written yet. It really helps!", brush: "NgOrange"));
        if (cfg.Plan.Length > 0) why.Add(Line($"Plan: I will {cfg.Plan}"));
        why.Add(Line(cfg.CommitNights switch
        {
            null => "\U0001F3AF Not committed yet",
            0 => "\U0001F3AF Committed: forever ∞",
            { } n => $"\U0001F3AF Committed: {n} nights",
        }, strong: true));
        why.Add(Line($"Morning check-in: {(cfg.MorningCheckIn ? "on" : "off")}"));
        ReviewCards.Children.Add(ReviewCard("\U0001F4AC", "Your why", 1, [.. why]));

        // Bedtimes
        ReviewCards.Children.Add(ReviewCard("\U0001F6CF️", "Bedtimes", 2, Week(cfg)));

        // Warnings
        var warn = new List<UIElement>();
        if (cfg.Warnings.Count == 0) warn.Add(Line("No warnings. Bold move.", brush: "NgOrange"));
        foreach (var w in cfg.Warnings)
        {
            var p = Defaults.WarningPresets[Defaults.PresetIndex(w.MinutesBefore)];
            warn.Add(Line($"{p.Emoji}  {p.Name}: {w.MinutesBefore} min before{(w.Sound ? "  \U0001F514" : "")}", strong: true));
        }
        ReviewCards.Children.Add(ReviewCard("⏰", "Warnings", 3, [.. warn]));

        // At bedtime
        var (ae, an, _, _) = ActionInfo.TryGetValue(cfg.Action, out var info) ? info : ("\U0001F319", cfg.Action, 0, "");
        var act = new List<UIElement>
        {
            new TextBlock { Text = $"{ae}  {an}", FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6), Foreground = Res("NgMoon") },
            Line($"After a {Seconds(cfg.CountdownSeconds)} countdown"),
            Line($"Again every {cfg.RecheckMinutes} min if the PC comes back on"),
            Line(cfg.NightModeMinutes > 0 ? $"Settings lock {Minutes(cfg.NightModeMinutes)} before bedtime" : "No night mode"),
        };
        if (ScheduleMath.EndDate(cfg) is { } end) act.Add(Line($"Ends after {end:d MMM yyyy}"));
        ReviewCards.Children.Add(ReviewCard("\U0001F319", "At bedtime", 4, [.. act]));

        // Game and screen time
        var st = cfg.ScreenTime;
        static string F(int m) => m <= 0 ? "no limit" : NightGuard.Core.ScreenTime.ScreenTimeEngine.FormatMinutes(m);
        UIElement[] screen = st.Enabled
            ? [Line($"Games: {F(st.GameMinutesWeekday)} weekdays, {F(st.GameMinutesWeekend)} weekends", strong: true),
               Line($"Screen: {F(st.ScreenMinutesWeekday)} weekdays, {F(st.ScreenMinutesWeekend)} weekends"),
               Line($"Then: {st.LimitAction}. New day at {st.ResetHour}:00.")]
            : [Line("Off")];
        ReviewCards.Children.Add(ReviewCard("\U0001F3AE", "Game and screen time", 5, screen));

        // Blocking
        var b = cfg.Blocking;
        var block = new List<UIElement>();
        if (b.Enabled)
        {
            block.Add(Line($"{b.Sites.Count} sites, {b.Apps.Count} apps or folders", strong: true));
            if (b.DuringBedtime) block.Add(Line("During bedtime hours"));
            foreach (var w in b.Windows) block.Add(Line($"{string.Join(" ", w.Days)}: {TimeText.Format(w.From)} to {TimeText.Format(w.To)}"));
        }
        else block.Add(Line("Off"));
        ReviewCards.Children.Add(ReviewCard("\U0001F6AB", "Blocking", 6, [.. block]));

        // Trusted person
        ReviewCards.Children.Add(ReviewCard("\U0001F510", "Trusted person", 7, Line(TrustedOn
            ? "On: saving needs the code"
            : TrustCheck.IsChecked == true ? $"On: your trusted person sets the code when you click {SaveButton.Content}" : "Off", strong: TrustedOn || TrustCheck.IsChecked == true)));

        ConfirmCheck.IsEnabled = errs.Count == 0;

        // Show what the service will treat as looser than now
        if (Configured && errs.Count == 0 && _serviceError is null)
        {
            var check = await ServiceClient.SendAsync(new IpcRequest { Op = Ops.CheckSave, Config = cfg });
            if (check.Ok && check.Loosening is { Count: > 0 } loose)
            {
                LooseCard.Visibility = Visibility.Visible;
                LooseTitle.Text = TrustedOn ? "\U0001F513 Looser than now (allowed with the trusted person's code)" : "\U0001F6A7 Looser than now (needs the 7-day cool-off, or undo these)";
                LooseText.Text = string.Join("\n", loose.Select(l => "•  " + l));
            }
        }
    }

    private void ConfirmCheck_Changed(object sender, RoutedEventArgs e)
    {
        // A new trusted-person code: the risk warning comes first
        if (ConfirmCheck.IsChecked == true && TrustCheck.IsChecked == true && !TrustedOn && !CodeDialogs.Warning(this))
            ConfirmCheck.IsChecked = false;
        UpdateNav();
    }

    // ================================================================ saving
    private async Task<IpcResponse> RunAsync(IpcRequest req)
    {
        _busy = true;
        Cursor = System.Windows.Input.Cursors.Wait;
        UpdateNav();
        try { return await ServiceClient.SendAsync(req, timeoutMs: 60_000); }
        finally
        {
            _busy = false;
            Cursor = null;
            UpdateNav();
        }
    }

    private async Task<bool> RequireAdminAsync()
    {
        var s = await ServiceClient.SendAsync(Ops.Status);
        if (!s.Ok) { Ask.Error(this, s.Error ?? "Can't reach the NightGuard service."); return false; }
        if (s.Status!.NightModeReason is { } nm) { Ask.Warn(this, nm); return false; }
        if (s.Status.ClientIsAdmin) return true;
        if (Ask.YesNo(this, "Changing NightGuard needs an administrator. Open NightGuard as administrator now?\n\n(Changes on screen aren't carried over, so you'll make them again there.)"))
            OpenAsAdmin();
        return false;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var cfg = ReadForm();
        var errs = ConfigValidator.Validate(cfg);
        if (errs.Count > 0) { Ask.Warn(this, "Please fix these first:\n\n- " + string.Join("\n- ", errs)); return; }
        if (!await RequireAdminAsync()) return;

        string? code = null;
        if (TrustedOn)
        {
            code = CodeDialogs.AskCode(this, "Saving changes needs the code.");
            if (code is null) return;
        }

        string? newCode = null;
        if (TrustCheck.IsChecked == true && !TrustedOn)
        {
            newCode = CodeDialogs.NewCode(this, ContactBox.Text);
            if (newCode is null && !Ask.YesNo(this, "The trusted-person code was cancelled. Save without trusted-person mode?")) return;
        }

        if (cfg.Action == "Hibernate" && _status is { HibernateAvailable: false }
            && Ask.YesNo(this, "Hibernate is switched off in Windows on this PC.\n\nTurn it on now? Windows sets aside some disk space for it (a file about half the size of your RAM).\n\nIf you choose No, NightGuard will shut down instead of hibernating at bedtime."))
        {
            var h = await RunAsync(new IpcRequest { Op = Ops.EnableHibernate });
            if (!h.Ok) Ask.Warn(this, h.Error ?? "Hibernate couldn't be turned on.");
        }

        var firstStart = !Configured;
        var req = new IpcRequest { Op = Ops.Save, Config = cfg, Code = code, EnableTrusted = newCode is not null, NewCode = newCode, Contact = ContactBox.Text.Trim() };
        var r = await RunAsync(req);

        if (!r.Ok && r.ErrorKind == ErrorKinds.Looser && r.Loosening is { Count: > 0 } loose)
        {
            var list = "\n  - " + string.Join("\n  - ", loose);
            switch (r.Status?.UnlockState)
            {
                case "Open":
                    if (!Ask.Sure(this, $"These changes make NightGuard looser:{list}\n\nYour cool-off is over, so this is allowed. It uses up your unlock.\n\nARE YOU SURE?")) return;
                    req.UseUnlock = true;
                    r = await RunAsync(req);
                    break;
                case "Waiting":
                    Ask.Warn(this, $"These changes make NightGuard looser:{list}\n\n{r.Error}\nUntil then you can only make things stricter. Undo the looser changes (Discard changes, bottom left), or come back then.");
                    return;
                default:
                    if (Ask.YesNo(this, $"These changes make NightGuard looser:{list}\n\nNightGuard is a commitment, so loosening it needs a 7-day cool-off. If you still want it in 7 days, you'll have 3 days to make looser changes (or uninstall).\n\nRequest an unlock now? You can cancel it anytime.\n\n(Stricter changes save right away. Undo the looser ones if that's all you need.)"))
                    {
                        var u = await RunAsync(new IpcRequest { Op = Ops.RequestUnlock });
                        Ask.Info(this, u.Message ?? u.Error ?? "");
                        await ReloadAsync();
                    }
                    return;
            }
        }

        if (!r.Ok)
        {
            var msg = r.Error ?? "Not saved.";
            if (r.Problems is { Count: > 0 } p) msg += "\n\n- " + string.Join("\n- ", p);
            Ask.Warn(this, msg);
            return;
        }
        var done = r.Message ?? "Saved.";
        if (newCode is not null) done += "\n\nTrusted-person mode is on. From now on, changes and uninstall need the trusted person's code.";
        Steps.SelectedIndex = 0;
        await ReloadAsync();
        // Party time
        Confetti.Burst(Root);
        Confetti.Burst(Root, new Point(Root.ActualWidth / 2, Root.ActualHeight * 0.6), 120);
        await Task.Delay(firstStart ? 1800 : 900);
        Ask.Happy(this, firstStart ? "NightGuard is on duty! \U0001F989\n\n" + done : done);
    }

    private async void Discard_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void Journal_Click(object sender, RoutedEventArgs e) => new JournalWindow { Owner = this }.Show();
    private void Status_Click(object sender, RoutedEventArgs e) => new StatusWindow { Owner = this }.Show();

    // ================================================================ uninstall
    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var s = await ServiceClient.SendAsync(Ops.Status);
        if (!s.Ok || s.Status is not { } st) { Ask.Error(this, s.Error ?? "Can't reach the NightGuard service."); return; }
        if (st.NightModeReason is { } nm) { Ask.Warn(this, "Nice try. " + nm); return; }
        if (!st.TrustedEnabled && st.UninstallBlock is { } why)
        {
            if (st.EndDate is not null) Ask.Info(this, why);
            else if (st.UnlockState == "Waiting")
            {
                if (Ask.YesNo(this, why + "\n\nChanged your mind? Cancel the request?"))
                {
                    var c = await RunAsync(new IpcRequest { Op = Ops.CancelUnlock });
                    Ask.Info(this, c.Message ?? "");
                }
            }
            else if (await RequireAdminAsync()
                     && Ask.YesNo(this, "NightGuard is locked in. That's what makes it work.\n\nYou can request to uninstall. A 7-day cool-off starts now; after it you have 3 days to uninstall. You can cancel the request anytime.\n\nRequest to uninstall?"))
            {
                var u = await RunAsync(new IpcRequest { Op = Ops.RequestUnlock });
                Ask.Info(this, u.Message ?? u.Error ?? "");
            }
            await ReloadAsync();
            return;
        }
        if (!await RequireAdminAsync()) return;
        string? code = null;
        if (st.TrustedEnabled)
        {
            code = CodeDialogs.AskCode(this, "Uninstalling needs the code.");
            if (code is null) return;
        }
        if (!Ask.Sure(this, "ARE YOU SURE you want to uninstall NightGuard?\n\nThis removes its service, blocks and files. If it's late and you just want one more game, click No and go to bed.")) return;
        var r = await RunAsync(new IpcRequest { Op = Ops.Uninstall, Code = code });
        if (!r.Ok) { Ask.Warn(this, r.Error ?? "Not removed."); return; }
        Ask.Info(this, r.Message ?? "NightGuard is being removed.");
        Close();
    }
}
