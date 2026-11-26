using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NightGuard.Core.Blocking;
using NightGuard.Core.Config;
using NightGuard.Core.Ipc;
using NightGuard.Core.Schedule;

namespace NightGuard.App;

/// <summary>
/// The NightGuard window: a step-by-step wizard (steps on the left, one page at a time, Back / Next).
/// Nothing changes until the last page. Every save goes to the service, which checks night mode,
/// the self-lock and the trusted person's code itself.
/// </summary>
public partial class MainWindow : Window
{
    private sealed record NightRow(CheckBox On, TimePicker Bed, TimePicker Unlock, TextBlock Note);
    private sealed record WarnRow(CheckBox On, IntBox Minutes, CheckBox Sound, TextBox Message, Button Preview);
    private sealed record SlotRow(CheckBox On, CheckBox[] Days, TimePicker From, TimePicker To);

    private readonly List<NightRow> _nights = [];
    private readonly List<WarnRow> _warns = [];
    private readonly List<SlotRow> _slots = [];
    private readonly FrameworkElement[] _pages;
    private StatusInfo? _status;
    private string? _serviceError;
    private bool _loading = true;
    private bool _busy;

    private static readonly Dictionary<string, string> ActionDescriptions = new()
    {
        ["Shutdown"] = "Strongest. Closes everything; unsaved work is lost.",
        ["Sleep"] = "Medium. Quick to resume, but it goes back to sleep every few minutes during the night.",
        ["Hibernate"] = "Recommended. Saves your open apps to disk and powers off, so they are usually still there in the morning (not guaranteed, so save anyway). If hibernate is off in Windows, NightGuard offers to turn it on when you save.",
        ["Lock"] = "Weakest. Locks the screen, again every few minutes, but you can keep signing back in.",
        ["SignOut"] = "Medium. Closes all your apps and signs you out, again if you sign back in.",
    };

    // A little encouragement in the corner, one per page
    private static readonly string[] Tips =
    [
        "Sleep isn't lost gaming time. It's how you get better at the game. \U0001F9E0",
        "Most people need 7 to 9 hours. Your K/D ratio needs them too.",
        "Screens off 30 minutes before bed helps you fall asleep faster. \U0001F4F5",
        "Hibernate keeps your apps where you left them. Shutdown keeps nothing. Choose wisely.",
        "\"Why\" beats willpower. Write yours down: it really helps.",
        "A budget isn't a punishment. It's a plan. \U0001F4C5",
        "Blocking the 1 AM rabbit holes is easier than resisting them.",
        "Someone you trust holding the key? That's not weakness, that's strategy. \U0001F91D",
        "Stricter is instant, looser takes a week. Past-you made a deal. \U0001F91D",
    ];

    public MainWindow()
    {
        InitializeComponent();
        _pages = [PageWelcome, PageBedtimes, PageWarnings, PageAction, PageWhy, PageScreen, PageBlock, PageTrusted, PageReview];
        TimeHintText.Text = TimeText.Hint;
        foreach (var a in Defaults.Actions) ActionBox.Items.Add(a);
        foreach (var a in Defaults.ScreenActions) LimitAction.Items.Add(a);
        QuickBed.Value = "23:00";
        QuickUnlock.Value = "06:00";
        BuildNightRows();
        BuildWarningRows();
        BuildSlotRows();

        // Any change on any page: re-check the settings and update what depends on what
        Pages.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => OnFormChanged()));
        Pages.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => OnFormChanged()));
        Pages.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => OnFormChanged()));
        Pages.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => OnFormChanged()));

        Steps.SelectedIndex = 0;
        Loaded += async (_, _) => await ReloadAsync();
    }

    // ================================================================ building the repeating rows
    private void BuildNightRows()
    {
        NightsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        NightsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        NightsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        NightsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddHeaderRow(NightsGrid, "Night", "Bedtime", "Unlocks at", "");
        for (var i = 0; i < 7; i++)
        {
            var row = new NightRow(
                new CheckBox { Content = Defaults.DayNames[i] + " night", VerticalAlignment = VerticalAlignment.Center, FontSize = 14 },
                new TimePicker { Margin = new Thickness(0, 0, 18, 0) },
                new TimePicker { Margin = new Thickness(0, 0, 18, 0) },
                new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            row.Bed.ValueChanged += (_, _) => OnFormChanged();
            row.Unlock.ValueChanged += (_, _) => OnFormChanged();
            AddRow(NightsGrid, row.On, row.Bed, row.Unlock, row.Note);
            _nights.Add(row);
        }
    }

    private void BuildWarningRows()
    {
        foreach (var w in new[] { 60, 110, 70, 1, 90 }) WarningsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        WarningsGrid.ColumnDefinitions[3] = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        AddHeaderRow(WarningsGrid, "On", "Minutes before", "Sound", "Message", "");
        for (var i = 0; i < 6; i++)
        {
            var row = new WarnRow(
                new CheckBox { VerticalAlignment = VerticalAlignment.Center },
                new IntBox { Min = 1, Max = 360, Width = 80 },
                new CheckBox { VerticalAlignment = VerticalAlignment.Center, IsChecked = true },
                new TextBox { Margin = new Thickness(0, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center },
                new Button { Content = "Preview", Padding = new Thickness(10, 5, 10, 5), Tag = i });
            row.Preview.Click += PreviewWarning_Click;
            AddRow(WarningsGrid, row.On, row.Minutes, row.Sound, row.Message, row.Preview);
            _warns.Add(row);
        }
    }

    private void BuildSlotRows()
    {
        SlotsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        for (var d = 0; d < 7; d++) SlotsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        SlotsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SlotsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddHeaderRow(SlotsGrid, ["On", .. Defaults.Days, "From", "To"]);
        for (var i = 0; i < 4; i++)
        {
            var days = Enumerable.Range(0, 7).Select(d => new CheckBox { IsChecked = d < 5, MinWidth = 0 }).ToArray();
            var row = new SlotRow(new CheckBox { MinWidth = 0 }, days, new TimePicker { Margin = new Thickness(6, 0, 12, 0) }, new TimePicker());
            row.From.Value = "09:00";
            row.To.Value = "17:00";
            row.From.ValueChanged += (_, _) => OnFormChanged();
            row.To.ValueChanged += (_, _) => OnFormChanged();
            AddRow(SlotsGrid, [row.On, .. days, row.From, row.To]);
            _slots.Add(row);
        }
    }

    private static void AddHeaderRow(Grid g, params string[] headers)
    {
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < headers.Length; c++)
        {
            var t = new TextBlock { Text = headers[c], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetColumn(t, c);
            g.Children.Add(t);
        }
    }

    private static void AddRow(Grid g, params UIElement[] cells)
    {
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var r = g.RowDefinitions.Count - 1;
        for (var c = 0; c < cells.Length; c++)
        {
            if (cells[c] is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 4, 0, 4);
            else if (cells[c] is FrameworkElement fe2) fe2.Margin = new Thickness(fe2.Margin.Left, 4, fe2.Margin.Right, 4);
            Grid.SetRow(cells[c], r);
            Grid.SetColumn(cells[c], c);
            g.Children.Add(cells[c]);
        }
    }

    // ================================================================ navigation
    private int PageIndex => Steps.SelectedIndex < 0 ? 0 : Steps.SelectedIndex;

    private void Steps_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages is null) return;
        for (var i = 0; i < _pages.Length; i++) _pages[i].Visibility = i == PageIndex ? Visibility.Visible : Visibility.Collapsed;
        if (_pages[PageIndex] == PageReview) _ = UpdateReviewAsync();
        UpdateNav();
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (PageIndex > 0) Steps.SelectedIndex = PageIndex - 1; }
    private void Next_Click(object sender, RoutedEventArgs e) { if (PageIndex < _pages.Length - 1) Steps.SelectedIndex = PageIndex + 1; }

    private void UpdateNav()
    {
        if (_pages is null) return;
        var i = PageIndex;
        var last = _pages.Length - 1;
        BackButton.IsEnabled = i > 0;
        NextButton.Visibility = i < last ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = i == 0 ? "Let's go \U0001F680" : i == last - 1 ? "Review \U0001F440" : "Next \u2192";
        TipText.Text = Tips[i % Tips.Length];
        // Save only on the last page, and only once the settings are confirmed
        SaveButton.Visibility = i == last && ConfirmCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = !_busy && _serviceError is null;
    }

    // ================================================================ talking to the service
    private async Task ReloadAsync()
    {
        _loading = true;
        var status = await ServiceClient.SendAsync(Ops.Status);
        _serviceError = status.Ok ? null : status.Error;
        _status = status.Status;
        NightGuardConfig cfg = new();
        if (status.Ok)
        {
            var c = await ServiceClient.SendAsync(Ops.GetConfig);
            if (c.Ok && c.Config is not null) cfg = c.Config;
        }
        LoadForm(cfg);
        TrustCheck.IsChecked = _status?.TrustedEnabled == true;
        ContactBox.Text = _status?.TrustedContact ?? "";
        _loading = false;
        UpdateWelcome();
        UpdateBanner();
        UpdateTrustPage();
        OnFormChanged();
        if (_pages[PageIndex] == PageReview) await UpdateReviewAsync();
        UpdateNav();
    }

    private bool Configured => _status?.Configured == true;
    private bool TrustedOn => _status?.TrustedEnabled == true;

    private void UpdateBanner()
    {
        BannerButton.Visibility = Visibility.Visible;
        if (_serviceError is not null)
        {
            Show(_serviceError, "Try again", red: true);
        }
        else if (_status?.Uninstalling == true)
        {
            Show("NightGuard is being removed.", null, red: true);
        }
        else if (_status?.NightModeReason is { } nm)
        {
            Show(nm, null, red: false);
        }
        else if (_status is { ClientIsAdmin: false })
        {
            Show("You're signed in with a standard account. You can look around and preview, but saving changes needs an administrator.", "Open as administrator", red: false);
        }
        else
        {
            Banner.Visibility = Visibility.Collapsed;
        }

        void Show(string text, string? button, bool red)
        {
            Banner.Visibility = Visibility.Visible;
            Banner.Background = (System.Windows.Media.Brush)FindResource(red ? "NgRed" : "NgIndigo");
            BannerText.Text = text;
            BannerButton.Content = button;
            BannerButton.Visibility = button is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async void BannerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceError is not null) { await ReloadAsync(); return; }
        OpenAsAdmin();
    }

    private bool OpenAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "-settings") { UseShellExecute = true, Verb = "runas" });
            Close();
            return true;
        }
        catch (Win32Exception) { return false; }   // the UAC prompt was cancelled
    }

    // ================================================================ welcome page
    private const string WelcomeNew =
        "NightGuard is your bedtime bouncer. You tell it when you want to be in bed, and it makes sure your PC agrees with you. No more \"one more match\" at 2 AM.\n\n" +
        "We'll walk through a few short steps together:\n\n" +
        "   \U0001F6CF\uFE0F  Bedtimes: when your nights start and end.\n" +
        "   \u23F0  Warnings: friendly (then less friendly) popups before bedtime.\n" +
        "   \U0001F50C  At bedtime: what your PC does when time is up.\n" +
        "   \U0001F4AC  Your why and journal: your reason, a morning cheer, streaks and badges.\n" +
        "   \U0001F3AE  Game and screen time: daily budgets, if you want them.\n" +
        "   \U0001F6AB  Block sites and apps: goodbye, 1 AM doomscrolling (optional).\n" +
        "   \U0001F510  Trusted person: hand the off switch to someone else (optional).\n" +
        "   \u2705  Review and save: check everything, then switch it on.\n\n" +
        "The defaults are sensible, so if you're not sure about something, just click Next. Nothing starts until the very last page.\n\n" +
        "Ready? Click \"Let's go\". Future you says thanks. \U0001F64C\n\n" +
        "\u26A0\uFE0F Heads up: NightGuard really does shut down, hibernate and lock this PC. It comes with no warranty, and you use it at your own risk, so save your work at the warnings.";

    private void UpdateWelcome()
    {
        var s = _status;
        if (s is { Configured: true })
        {
            Scene.Headline = "NightGuard is on duty \U0001F6E1\uFE0F";
            Scene.Tagline = s.Streak > 1 ? $"{s.Streak} clean nights in a row. Look at you go! \U0001F525" : "Your nights are in good hands. Sleep tight!";
            WelcomeText.Text = "It's guarding your nights. No take-backs: that's the point.\n\n" +
                "You can always make it STRICTER (earlier bedtime, more blocking, less game time) and save right away. " +
                "Making it LOOSER works like quitting anything else: you ask, wait 7 days, and then decide with a clear head.";
            var next = s.CurrentNight is { } cur ? $"Locked right now, until {cur.End:dddd} {TimeText.Format(cur.End)}."
                     : s.NextNight is { } n ? $"Next locked night: {n.Start:dddd} {TimeText.Format(n.Start)} until {n.End:dddd} {TimeText.Format(n.End)}."
                     : "No locked night scheduled.";
            var end = s.EndDate is { } ed && DateTime.TryParseExact(ed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? $"Last night: {d:dddd d MMMM yyyy}. After that, NightGuard removes itself. It can't be uninstalled before then."
                : "No end date: it runs until you uninstall it (after a 7-day cool-off).";
            var lockText = s.TrustedEnabled ? "Trusted-person mode is on: changes and uninstall need your trusted person's code." : s.UnlockText;
            var streak = s.Streak > 0 ? $"\U0001F525 {s.Streak} clean night(s) in a row." : "\U0001F331 Your streak starts tonight.";
            StatusCardText.Text = string.Join("\n", new[] { next, end, lockText, streak, s.SafetyStandDown is { } sd ? $"Standing down: {sd}" : "" }.Where(x => x.Length > 0));
            StatusCard.Visibility = Visibility.Visible;
        }
        else
        {
            Scene.Headline = "Hi there, night owl! \U0001F989";
            Scene.Tagline = "Let's get you to bed on time. Future you is already grateful.";
            WelcomeText.Text = WelcomeNew;
            StatusCard.Visibility = _status?.SafetyStandDown is { } p ? Visibility.Visible : Visibility.Collapsed;
            StatusCardText.Text = _status?.SafetyStandDown is { } p2 ? $"The saved settings can't be used ({p2}). Saving new ones fixes it." : "";
        }
        SaveButton.Content = Configured ? "Save and apply \u2728" : "Start NightGuard \U0001F319";
        JournalButton.IsEnabled = StatusButton.IsEnabled = _serviceError is null;
        UninstallButton.IsEnabled = Configured;
        UninstallButton.Content = !Configured || TrustedOn ? "Uninstall..." : s?.UnlockState switch
        {
            "Open" => "Uninstall...",
            "Waiting" => "Cool-off running...",
            _ => "Request uninstall...",
        };
    }

    // ================================================================ form <-> settings
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
        int[] defaultMinutes = [30, 5, 60, 15, 10, 2];
        var used = cfg.Warnings.Select(w => w.MinutesBefore).ToList();
        var spare = defaultMinutes.Concat(Enumerable.Range(1, 360)).Where(m => !used.Contains(m)).Distinct().GetEnumerator();
        for (var i = 0; i < 6; i++)
        {
            var row = _warns[i];
            if (i < cfg.Warnings.Count)
            {
                var w = cfg.Warnings[i];
                row.On.IsChecked = true;
                row.Minutes.Value = w.MinutesBefore;
                row.Sound.IsChecked = w.Sound;
                row.Message.Text = w.Message;
            }
            else
            {
                spare.MoveNext();
                row.On.IsChecked = false;
                row.Minutes.Value = spare.Current;
                row.Sound.IsChecked = true;
                row.Message.Text = Defaults.WarningGeneric;
            }
        }
        ActionBox.SelectedItem = Defaults.Actions.Contains(cfg.Action) ? cfg.Action : "Hibernate";
        CountdownBox.Value = cfg.CountdownSeconds;
        RecheckBox.Value = cfg.RecheckMinutes;
        NightModeBox.Value = cfg.NightModeMinutes;
        var end = ScheduleMath.EndDate(cfg);
        EndDateCheck.IsChecked = end is not null;
        EndDatePicker.DisplayDateStart = end is { } e0 && e0 < DateTime.Today ? e0 : DateTime.Today;
        EndDatePicker.SelectedDate = end ?? DateTime.Today.AddDays(30);
        EndDatePicker.IsEnabled = end is not null;
        WhyBox.Text = cfg.Why;
        CheckInCheck.IsChecked = cfg.MorningCheckIn;
        DesktopCheck.IsChecked = cfg.ShortcutDesktop;

        var b = cfg.Blocking;
        BlockCheck.IsChecked = b.Enabled;
        BlockBedtimeCheck.IsChecked = b.DuringBedtime;
        SitesBox.Text = string.Join(Environment.NewLine, b.Sites);
        AppsList.Items.Clear();
        foreach (var a in b.Apps) AppsList.Items.Add(a);
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
        ScreenCheck.IsChecked = st.Enabled;
        GameWd.Value = st.GameMinutesWeekday;
        GameWe.Value = st.GameMinutesWeekend;
        ScreenWd.Value = st.ScreenMinutesWeekday;
        ScreenWe.Value = st.ScreenMinutesWeekend;
        ResetHour.Value = st.ResetHour;
        GraceBox.Value = st.ShutdownGraceMinutes;
        LimitAction.SelectedItem = Defaults.ScreenActions.Contains(st.LimitAction) ? st.LimitAction : "Lock";
        WarnAtBox.Text = string.Join(", ", st.WarnAtMinutesLeft);
        LaunchersCheck.IsChecked = st.CloseLaunchers;
        GameFolders.Text = string.Join(Environment.NewLine, st.GameFolders);
        GameProcs.Text = string.Join(Environment.NewLine, st.GameProcesses);
        IgnoreProcs.Text = string.Join(Environment.NewLine, st.IgnoreProcesses);
        LauncherProcs.Text = string.Join(Environment.NewLine, st.LauncherProcesses);
        UsageText.Text = _status is { Configured: true, ScreenTimeEnabled: true } ? _status.UsageText : "";
        _loading = false;
    }

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private NightGuardConfig ReadForm()
    {
        var cfg = new NightGuardConfig();
        for (var i = 0; i < 7; i++)
            cfg.Schedule[Defaults.Days[i]] = new NightSchedule { Enabled = _nights[i].On.IsChecked == true, Bedtime = _nights[i].Bed.Value, Unlock = _nights[i].Unlock.Value };
        cfg.Warnings = _warns.Where(w => w.On.IsChecked == true)
            .Select(w => new WarningSetting { MinutesBefore = w.Minutes.Value, Sound = w.Sound.IsChecked == true, Message = w.Message.Text }).ToList();
        cfg.Action = ActionBox.SelectedItem as string ?? "Hibernate";
        cfg.CountdownSeconds = CountdownBox.Value;
        cfg.RecheckMinutes = RecheckBox.Value;
        cfg.NightModeMinutes = NightModeBox.Value;
        cfg.EndDate = EndDateCheck.IsChecked == true && EndDatePicker.SelectedDate is { } ed ? ed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        cfg.Why = WhyBox.Text.Trim();
        cfg.MorningCheckIn = CheckInCheck.IsChecked == true;
        cfg.ShortcutStartMenu = true;
        cfg.ShortcutDesktop = DesktopCheck.IsChecked == true;
        cfg.Blocking = new BlockingSettings
        {
            Enabled = BlockCheck.IsChecked == true,
            DuringBedtime = BlockBedtimeCheck.IsChecked == true,
            Sites = Lines(SitesBox.Text).Select(s => s.ToLowerInvariant()).ToList(),
            Apps = AppsList.Items.Cast<string>().ToList(),
            Windows = _slots.Where(s => s.On.IsChecked == true).Select(s => new BlockWindow
            {
                Days = Enumerable.Range(0, 7).Where(d => s.Days[d].IsChecked == true).Select(d => Defaults.Days[d]).ToList(),
                From = s.From.Value, To = s.To.Value,
            }).ToList(),
        };
        cfg.ScreenTime = new ScreenTimeSettings
        {
            Enabled = ScreenCheck.IsChecked == true,
            ResetHour = ResetHour.Value,
            GameMinutesWeekday = GameWd.Value, GameMinutesWeekend = GameWe.Value,
            ScreenMinutesWeekday = ScreenWd.Value, ScreenMinutesWeekend = ScreenWe.Value,
            WarnAtMinutesLeft = WarnAtBox.Text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1).ToList(),
            LimitAction = LimitAction.SelectedItem as string ?? "Lock",
            ShutdownGraceMinutes = GraceBox.Value,
            CloseLaunchers = LaunchersCheck.IsChecked == true,
            GameFolders = Lines(GameFolders.Text),
            GameProcesses = Lines(GameProcs.Text),
            IgnoreProcesses = Lines(IgnoreProcs.Text),
            LauncherProcesses = Lines(LauncherProcs.Text),
        };
        return ConfigSerializer.Normalize(cfg);
    }

    // ================================================================ live checks
    private void OnFormChanged()
    {
        if (_loading || _pages is null) return;
        for (var i = 0; i < 7; i++)
        {
            var r = _nights[i];
            var on = r.On.IsChecked == true;
            r.Bed.IsEnabled = r.Unlock.IsEnabled = on;
            if (!on) { r.Note.Text = "\U0001F389 free night"; r.Note.Foreground = null; continue; }
            var hours = ScheduleMath.LockedHours(r.Bed.Value, r.Unlock.Value);
            var text = hours is { } h ? $"\U0001F634 locked {h:0.#} h" : "\u26A0\uFE0F unlock must be after bedtime";
            if (ScheduleMath.ParseTime(r.Bed.Value).TotalHours < 12) text += ", early " + Defaults.DayNames[(i + 1) % 7][..3];
            r.Note.Text = text;
            r.Note.Foreground = hours is null or > 16 ? (System.Windows.Media.Brush)FindResource("NgRed") : null;
        }
        foreach (var w in _warns) w.Minutes.IsEnabled = w.Sound.IsEnabled = w.Message.IsEnabled = w.Preview.IsEnabled = w.On.IsChecked == true;
        ScreenBody.IsEnabled = ScreenCheck.IsChecked == true;
        GraceBox.IsEnabled = LimitAction.SelectedItem as string == "Shutdown";
        BlockBody.IsEnabled = BlockCheck.IsChecked == true;
        foreach (var s in _slots)
        {
            var on = s.On.IsChecked == true;
            foreach (var d in s.Days) d.IsEnabled = on;
            s.From.IsEnabled = s.To.IsEnabled = on;
        }
        if (_pages[PageIndex] != PageReview && ConfirmCheck.IsChecked == true) ConfirmCheck.IsChecked = false;

        var errs = ConfigValidator.Validate(ReadForm());
        if (errs.Count > 0)
        {
            BottomHint.Foreground = (System.Windows.Media.Brush)FindResource("NgRed");
            BottomHint.Text = "Fix before saving: " + errs[0] + (errs.Count > 1 ? $" (+{errs.Count - 1} more)" : "");
        }
        else
        {
            BottomHint.ClearValue(TextBlock.ForegroundProperty);
            BottomHint.Text = Configured
                ? "Stricter changes save right away; looser ones need the 7-day cool-off."
                : "Nothing starts until the last page. Jump to any step on the left.";
        }
    }

    private void ActionBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ActionDesc.Text = ActionBox.SelectedItem is string a && ActionDescriptions.TryGetValue(a, out var d) ? d : "";

    private void LimitAction_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnFormChanged();
    private void Section_Changed(object sender, RoutedEventArgs e) => OnFormChanged();
    private void EndDate_Changed(object sender, RoutedEventArgs e) => EndDatePicker.IsEnabled = EndDateCheck.IsChecked == true;

    private void WhyBox_TextChanged(object sender, TextChangedEventArgs e) =>
        WhyCount.Text = $"{WhyBox.Text.Length} / 300" + (WhyBox.Text.Trim().Length == 0 ? "   (empty: no reminder will be shown)" : "");

    // ================================================================ bedtimes page
    private void SetNights(IEnumerable<string> days)
    {
        foreach (var d in days)
        {
            var i = Array.IndexOf(Defaults.Days, d);
            _nights[i].On.IsChecked = true;
            _nights[i].Bed.Value = QuickBed.Value;
            _nights[i].Unlock.Value = QuickUnlock.Value;
        }
        OnFormChanged();
    }

    private void QuickAll_Click(object sender, RoutedEventArgs e) => SetNights(Defaults.Days);
    private void QuickWeek_Click(object sender, RoutedEventArgs e) => SetNights(["Sun", "Mon", "Tue", "Wed", "Thu"]);
    private void QuickWeekend_Click(object sender, RoutedEventArgs e) => SetNights(["Fri", "Sat"]);

    // ================================================================ warnings page and previews
    private void Examples_Click(object sender, RoutedEventArgs e)
    {
        if (!Ask.YesNo(this, "Replace the messages of the ticked popups with example messages that match their timing?")) return;
        foreach (var w in _warns.Where(w => w.On.IsChecked == true))
            w.Message.Text = Defaults.WarningExamples.First(x => w.Minutes.Value >= x.MinMinutes).Message;
    }

    private void PreviewWarning_Click(object sender, RoutedEventArgs e)
    {
        var row = _warns[(int)((Button)sender).Tag];
        var cfg = ReadForm();
        var now = DateTime.Now;
        var night = ScheduleMath.NextNight(cfg, now) ?? ScheduleMath.GetNight(cfg, now, ignoreEndDate: true)
                    ?? new Night("", now.Date, now.Date.AddDays(1), now.Date.AddDays(1).AddHours(6));
        var text = ScheduleMath.FormatMessage(row.Message.Text, cfg, night, row.Minutes.Value, "t");
        var ticked = _warns.Where(w => w.On.IsChecked == true).Select(w => w.Minutes.Value).DefaultIfEmpty(row.Minutes.Value).Min();
        if (row.Minutes.Value <= ticked && cfg.Why.Length > 0) text += $"\n\nRemember why you started:\n“{cfg.Why}”";
        var urgent = row.Minutes.Value <= 5;
        if (row.Sound.IsChecked == true) System.Media.SystemSounds.Exclamation.Play();
        new AlertWindow(urgent ? "Final call!" : "Bedtime is coming", text, urgent, 0, preview: true) { Owner = this }.Show();
    }

    private void PreviewCountdown_Click(object sender, RoutedEventArgs e)
    {
        var cfg = ReadForm();
        new CountdownWindow(20, Defaults.ActionWord(cfg.Action), cfg.Why, preview: true).Show();
    }

    // ================================================================ screen time and blocking pages
    private bool IsProtected(string path, string what)
    {
        if (!PathRules.ForThisPc().IsProtected(path)) return false;
        Ask.Warn(this, $"That {what} is part of Windows and can't be used here.");
        return true;
    }

    private void AddGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pick a folder where games are installed" };
        if (dlg.ShowDialog(this) != true || IsProtected(dlg.FolderName, "folder")) return;
        GameFolders.Text = string.Join(Environment.NewLine, Lines(GameFolders.Text).Append(dlg.FolderName));
    }

    private void SocialPreset_Click(object sender, RoutedEventArgs e)
    {
        var have = Lines(SitesBox.Text).Select(s => s.ToLowerInvariant()).ToList();
        SitesBox.Text = string.Join(Environment.NewLine, have.Concat(Defaults.SocialPreset.Where(s => !have.Contains(s))));
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
        if (dlg.ShowDialog(this) != true || IsProtected(dlg.FileName, "program")) return;
        if (!AppsList.Items.Contains(dlg.FileName)) AppsList.Items.Add(dlg.FileName);
        OnFormChanged();
    }

    private void AddAppFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Every program inside this folder will be blocked (for example a Steam or Discord folder)" };
        if (dlg.ShowDialog(this) != true || IsProtected(dlg.FolderName, "folder")) return;
        if (!AppsList.Items.Contains(dlg.FolderName)) AppsList.Items.Add(dlg.FolderName);
        OnFormChanged();
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (AppsList.SelectedIndex >= 0) AppsList.Items.RemoveAt(AppsList.SelectedIndex);
        OnFormChanged();
    }

    // ================================================================ trusted person page
    private void UpdateTrustPage()
    {
        TrustCheck.IsEnabled = !TrustedOn;
        ContactBox.IsEnabled = TrustCheck.IsChecked == true && !TrustedOn;
        ChangeCodeButton.Visibility = TrustOffButton.Visibility = TrustedOn ? Visibility.Visible : Visibility.Collapsed;
        if (TrustedOn)
        {
            TrustState.Text = "Trusted-person mode is ON. Saving changes or uninstalling needs the code.";
            TrustState.Foreground = (System.Windows.Media.Brush)FindResource("NgGreen");
        }
        else if (TrustCheck.IsChecked == true)
        {
            TrustState.Text = "Your trusted person sets the code when you click " + SaveButton.Content + " on the last page, so have them next to you then.";
            TrustState.Foreground = (System.Windows.Media.Brush)FindResource("NgAccent");
        }
        else
        {
            TrustState.Text = "Off. Anyone with an administrator account on this PC can change the settings (within the self-lock rules).";
            TrustState.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    private void TrustCheck_Changed(object sender, RoutedEventArgs e) { if (!_loading) UpdateTrustPage(); }

    private async void ChangeCode_Click(object sender, RoutedEventArgs e)
    {
        if (!await RequireAdminAsync()) return;
        var old = CodeDialogs.AskCode(this, "To change the code, first enter the current one.");
        if (old is null || !CodeDialogs.Warning(this)) return;
        var code = CodeDialogs.NewCode(this, _status?.TrustedContact);
        if (code is null) { Ask.Info(this, "Cancelled. The old code still works."); return; }
        var r = await RunAsync(new IpcRequest { Op = Ops.SetCode, Code = old, NewCode = code });
        if (r.Ok) Ask.Info(this, r.Message ?? "Done.");
        else Ask.Warn(this, r.Error ?? "Couldn't change the code.");
    }

    private async void TrustOff_Click(object sender, RoutedEventArgs e)
    {
        if (!await RequireAdminAsync()) return;
        var code = CodeDialogs.AskCode(this, "Turning off trusted-person mode needs the code.");
        if (code is null) return;
        var r = await RunAsync(new IpcRequest { Op = Ops.DisableCode, Code = code });
        if (r.Ok) Ask.Info(this, r.Message ?? "Done.");
        else Ask.Warn(this, r.Error ?? "Couldn't turn it off.");
        await ReloadAsync();
    }

    // ================================================================ review page
    private async Task UpdateReviewAsync()
    {
        ConfirmCheck.IsChecked = false;
        var cfg = ReadForm();
        var errs = ConfigValidator.Validate(cfg);
        var sb = new StringBuilder();
        void Add(string l = "") => sb.AppendLine(l);
        if (errs.Count > 0)
        {
            Add("\u26A0\uFE0F  PLEASE FIX THESE FIRST:");
            foreach (var e in errs) Add("  - " + e);
            Add();
        }
        Add("\U0001F6CF\uFE0F  BEDTIMES");
        for (var i = 0; i < 7; i++)
        {
            var s = cfg.Schedule[Defaults.Days[i]];
            var name = $"{Defaults.DayNames[i]} night".PadRight(17);
            Add(s.Enabled ? $"  {name} {TimeText.Format(s.Bedtime)} until {TimeText.Format(s.Unlock)}" : $"  {name} free");
        }
        Add();
        Add("\u23F0  WARNINGS");
        if (cfg.Warnings.Count == 0) Add("  none");
        foreach (var w in cfg.Warnings) Add($"  {w.MinutesBefore} min before bedtime{(w.Sound ? ", with sound" : "")}");
        Add();
        Add("\U0001F50C  AT BEDTIME");
        Add($"  {cfg.Action} after a {cfg.CountdownSeconds} second countdown, repeating every {cfg.RecheckMinutes} min through the night");
        Add($"  Settings locked from {cfg.NightModeMinutes} min before bedtime until unlock");
        Add($"  End date: {(cfg.EndDate is { } ed ? $"last night is {ed}, then NightGuard removes itself" : "none")}");
        Add();
        Add("\U0001F4AC  YOUR WHY AND JOURNAL");
        Add(cfg.Why.Length > 0 ? $"  \"{cfg.Why}\"" : "  (no reason written; we recommend one!)");
        Add($"  Morning check-in, streaks and badges: {(cfg.MorningCheckIn ? "on" : "off")}");
        Add();
        Add("\U0001F3AE  GAME AND SCREEN TIME");
        var st = cfg.ScreenTime;
        if (st.Enabled)
        {
            static string F(int m) => m <= 0 ? "no limit" : NightGuard.Core.ScreenTime.ScreenTimeEngine.FormatMinutes(m);
            Add($"  Games : {F(st.GameMinutesWeekday)} weekdays, {F(st.GameMinutesWeekend)} weekends");
            Add($"  Screen: {F(st.ScreenMinutesWeekday)} weekdays, {F(st.ScreenMinutesWeekend)} weekends, then {st.LimitAction}");
            Add($"  New day starts at {st.ResetHour}:00");
        }
        else Add("  off");
        Add();
        Add("\U0001F6AB  BLOCKING");
        var b = cfg.Blocking;
        if (b.Enabled)
        {
            Add($"  {b.Sites.Count} sites, {b.Apps.Count} apps/folders");
            if (b.DuringBedtime) Add("  during bedtime hours");
            foreach (var w in b.Windows) Add($"  {string.Join(" ", w.Days)}: {TimeText.Format(w.From)} to {TimeText.Format(w.To)}");
        }
        else Add("  off");
        Add();
        Add("\U0001F510  TRUSTED PERSON");
        Add(TrustedOn ? "  ON, saving needs the code" : TrustCheck.IsChecked == true ? $"  ON: your trusted person sets the code when you click {SaveButton.Content}" : "  off");

        ConfirmCheck.IsEnabled = errs.Count == 0;
        ReviewBox.Text = sb.ToString();

        // Show what the service will treat as looser than now
        if (Configured && errs.Count == 0 && _serviceError is null)
        {
            var check = await ServiceClient.SendAsync(new IpcRequest { Op = Ops.CheckSave, Config = cfg });
            if (check.Ok && check.Loosening is { Count: > 0 } loose)
            {
                var head = new StringBuilder();
                head.AppendLine(TrustedOn ? "\U0001F513 LOOSER THAN NOW (allowed with the trusted person's code):" : "\U0001F6A7 LOOSER THAN NOW (needs the 7-day cool-off, or undo these):");
                foreach (var l in loose) head.AppendLine("  - " + l);
                head.AppendLine();
                ReviewBox.Text = head + ReviewBox.Text;
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
        Ask.Happy(this, done);
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
