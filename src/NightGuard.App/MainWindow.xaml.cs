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

/// <summary>
/// The NightGuard window: home (once it's running) and a step-by-step setup (steps on the left,
/// one page at a time, Back / Next). Nothing changes until the last page. Every save goes to the
/// service, which checks night mode, the self-lock and the trusted person's code itself.
/// </summary>
public partial class MainWindow : Window
{
    private sealed record NightCard(CheckBox On, TimePicker Bed, TimePicker Unlock, TextBlock Note, ColumnDefinition Before, ColumnDefinition Locked, ColumnDefinition After, Border Fill);
    private sealed record WarnCard(Defaults.WarningPreset Preset, CheckBox On, PillPicker Minutes, CheckBox Sound, TextBox Custom, Button Preview, StackPanel Body);
    private sealed record SlotCard(CheckBox On, ToggleButton[] Days, TimePicker From, TimePicker To, StackPanel Body);

    private readonly List<NightCard> _nights = [];
    private readonly List<WarnCard> _warns = [];
    private readonly List<SlotCard> _slots = [];
    private readonly Dictionary<string, ToggleButton> _actionCards = [];
    private readonly List<ToggleButton> _commitCards = [];
    private readonly ToggleButton _customCommitCard = new();
    private readonly IntBox _commitCustom = new() { Min = 1, Max = 3650, Step = 1, Suffix = "nights" };
    private readonly FrameworkElement[] _pages;
    private StatusInfo? _status;
    private string? _serviceError;
    private bool _loading = true;
    private bool _busy;
    private bool _celebratedDone;

    // The commitment: what's picked on the Your why page, whether it's signed, and what was saved before
    private int? _commitChoice;
    private bool _signed;
    private bool _recommit;
    private int? _savedCommit;
    private string? _savedCommitStart;
    private string? _savedEndDate;

    private static readonly string[] ActionOrder = ["Hibernate", "Shutdown", "Sleep", "SignOut", "Lock"];
    private readonly Dictionary<string, TextBlock> _actionLocks = [];
    private string? _savedAction;   // the bedtime action NightGuard is running with (null before setup)
    private NightGuardConfig? _savedCfg;   // the settings NightGuard is running with (null before setup)

    private static readonly Dictionary<string, (string Emoji, string Name, int Strength, string Text)> ActionInfo = new()
    {
        ["Hibernate"] = ("🌙", "Hibernate", 3, "Saves your open apps and powers off. They're usually still there in the morning (save anyway). If hibernate is off in Windows, NightGuard offers to turn it on."),
        ["Shutdown"] = ("💀", "Shut down", 4, "The nuclear option. Everything closes, unsaved work is gone, and the PC goes properly dark. For the brave. 🦁"),
        ["Sleep"] = ("💤", "Sleep", 2, "Quick to resume, so it goes back to sleep every few minutes all night."),
        ["SignOut"] = ("🚪", "Sign out", 2, "Closes your apps and signs you out, again if you sign back in."),
        ["Lock"] = ("🔒", "Lock", 1, "Locks the screen every few minutes. You can keep signing back in."),
    };

    private static readonly string[] WhyExamples =
    [
        "I want to wake up without hating my alarm.",
        "I want energy for my mornings, not just my matches.",
        "My grades are worth more than my rank.",
        "I'm tired of being tired.",
        "3 AM me makes bad decisions.",
    ];

    private static readonly string[] PlanExamples =
    [
        "save, say gg, and stand up.",
        "plug my phone in across the room.",
        "brush my teeth right away.",
        "write tomorrow's first task down, then lights off.",
    ];

    public MainWindow()
    {
        InitializeComponent();
        var wa = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(1180, wa.Width * 0.92));
        Height = Math.Max(MinHeight, Math.Min(840, wa.Height * 0.92));

        _pages = [PageWelcome, PageWhy, PageBedtimes, PageWarnings, PageAction, PageScreen, PageBlock, PageTrusted, PageReview];
        var heads = new[] { HeadBedtimes, HeadWarnings, HeadAction, HeadScreen, HeadBlock, HeadTrusted, HeadReview };
        for (var i = 0; i < heads.Length; i++) heads[i].Step = $"Step {i + 2} of 8";
        WhyStep.Text = "STEP 1 OF 8  ·  YOUR WHY";
        TimeHintText.Text = TimeText.Hint;
        QuickBed.Value = "23:00";
        QuickUnlock.Value = "06:00";

        BuildHowItWorks();
        BuildRitual();
        BuildCommitCards();
        BuildNightCards();
        BuildWarningCards();
        BuildActionCards();
        SetupListEditors();
        BuildSlotCards();
        CountdownPills.SetChoices([(30, "30 s"), (60, "1 min"), (120, "2 min"), (300, "5 min")], Seconds);
        RecheckPills.SetChoices([(1, "1 min"), (2, "2 min"), (5, "5 min"), (10, "10 min"), (15, "15 min")], m => $"{m} min");
        NightModePills.SetChoices([(0, "Off"), (30, "30 min"), (60, "1 hour"), (120, "2 hours"), (240, "4 hours")], Minutes);
        LimitPills.SetChoices([(0, "🔒  Lock"), (1, "🔌  Shut down"), (2, "💬  Just warn")], i => Defaults.ScreenActions[i]);
        WarnAtPills.SetChoices([(30, "30 min"), (15, "15 min"), (10, "10 min"), (5, "5 min"), (1, "1 min")]);

        // Any change on any page: re-check the settings and update what depends on what
        Pages.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => OnFormChanged()));
        Pages.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => OnFormChanged()));
        Pages.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => OnFormChanged()));
        Pages.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => OnFormChanged()));

        SignButton.Signed += (_, _) => OnSigned();
        SizeChanged += (_, _) => UpdateCompact();
        WhySky.Loaded += (_, _) => { if (WhySky.Children.Count == 0) Art.Stars(WhySky, 1800, 2400, 170, 7); };
        Steps.SelectedIndex = 0;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private static string Seconds(int s) => s < 60 ? $"{s} s" : s % 60 == 0 ? $"{s / 60} min" : $"{s / 60} min {s % 60} s";
    private static string Minutes(int m) => m == 0 ? "Off" : m % 60 == 0 ? (m == 60 ? "1 hour" : $"{m / 60} hours") : $"{m} min";
    private Brush Res(string key) => (Brush)FindResource(key);

    private static TextBlock Text(string text, string style, double? size = null)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, style);
        if (size is { } s) t.FontSize = s;
        return t;
    }

    private static Border Badge(string emoji, double size = 44)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = Art.Emoji(emoji, size * 0.48, Brushes.White) };
        b.SetResourceReference(StyleProperty, "IconBadge");
        b.Width = b.Height = size;
        b.CornerRadius = new CornerRadius(size / 2);
        return b;
    }

    private static Border Card(UIElement child, double width, string style = "TileCard")
    {
        var b = new Border { Child = child };
        b.SetResourceReference(StyleProperty, style);
        if (width > 0) b.Width = width;
        return b;
    }

    // ================================================================ narrow windows: icons-only sidebar
    private void UpdateCompact()
    {
        var compact = ActualWidth < 1000;
        SideColumn.Width = new GridLength(compact ? 78 : 252);
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var t in Labels(Root)) t.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private static IEnumerable<TextBlock> Labels(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is TextBlock { Tag: "label" } t) yield return t;
            if (child == null || child is ScrollViewer) continue;   // skip the pages
            foreach (var x in Labels(child)) yield return x;
        }
    }

    // ================================================================ navigation
    private int PageIndex => Steps.SelectedIndex < 0 ? 0 : Steps.SelectedIndex;

    private void Steps_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages is null) return;
        for (var i = 0; i < _pages.Length; i++) _pages[i].Visibility = i == PageIndex ? Visibility.Visible : Visibility.Collapsed;
        if (_pages[PageIndex] is ScrollViewer sv) sv.ScrollToTop();
        if (_pages[PageIndex] == PageReview) _ = UpdateReviewAsync();
        UpdateNav();
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (PageIndex > 0) Steps.SelectedIndex = PageIndex - 1; }
    private void Next_Click(object sender, RoutedEventArgs e) { if (PageIndex < _pages.Length - 1) Steps.SelectedIndex = PageIndex + 1; }
    private void Go(int page) => Steps.SelectedIndex = page;

    private void UpdateNav()
    {
        if (_pages is null) return;
        var i = PageIndex;
        var last = _pages.Length - 1;
        BackButton.IsEnabled = i > 0;
        NextButton.Visibility = i < last ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = i == 0 ? (Configured ? "Settings →" : "Let's go \U0001F680") : i == last - 1 ? "Review \U0001F440" : "Next →";
        StepProgress.Value = i;
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
        _savedAction = Configured ? cfg.Action : null;
        _savedCfg = Configured ? cfg.Clone() : null;
        UpdateActionLocks();
        TrustCheck.IsChecked = _status?.TrustedEnabled == true;
        ContactBox.Text = _status?.TrustedContact ?? "";
        _loading = false;
        UpdateHome();
        UpdateBanner();
        UpdateTrustPage();
        OnFormChanged();
        if (_pages[PageIndex] == PageReview) await UpdateReviewAsync();
        UpdateNav();
        UpdateCompact();
    }

    private bool Configured => _status?.Configured == true;
    private bool TrustedOn => _status?.TrustedEnabled == true;

    private void UpdateBanner()
    {
        BannerButton.Visibility = Visibility.Visible;
        if (_serviceError is not null) Show(_serviceError, "Try again", red: true);
        else if (_status?.Uninstalling == true) Show("NightGuard is being removed.", null, red: true);
        else if (_status?.NightModeReason is { } nm) Show(nm, null, red: false);
        else if (_status is { ClientIsAdmin: false })
            Show("You're signed in with a standard account. You can look around and preview, but saving changes needs an administrator.", "Open as administrator", red: false);
        else Banner.Visibility = Visibility.Collapsed;

        void Show(string text, string? button, bool red)
        {
            Banner.Visibility = Visibility.Visible;
            Banner.Background = Res(red ? "NgRedDeep" : "NgIndigo");
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

    // ================================================================ welcome / home page
    private void UpdateHome()
    {
        var s = _status;
        var home = s is { Configured: true };
        NewUserPanel.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        HomePanel.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        WelcomeStepText.Text = home ? "Home" : "Welcome";
        StandDownCard.Visibility = s?.SafetyStandDown is not null ? Visibility.Visible : Visibility.Collapsed;
        StandDownText.Text = s?.SafetyStandDown is { } sd
            ? (home ? $"NightGuard is standing down: {sd}" : $"The saved settings can't be used ({sd}). Saving new ones fixes it.")
            : "";
        SaveButton.Content = Configured ? "Save and apply ✨" : "Start NightGuard \U0001F319";
        JournalButton.IsEnabled = StatusButton.IsEnabled = _serviceError is null;
        UninstallButton.IsEnabled = Configured;
        UninstallText.Text = !Configured || TrustedOn ? "Uninstall..." : s?.UnlockState switch
        {
            "Open" => "Uninstall...",
            "Waiting" => "Cool-off running...",
            _ => "Request uninstall...",
        };

        if (s is not { Configured: true })
        {
            Scene.Headline = "Hi there, night owl!";
            Scene.Tagline = "I'm Hoot. Let's get you to bed on time. Future you is already grateful.";
            Scene.OwlMood = OwlMood.Awake;
            return;
        }

        Scene.Headline = s.CurrentNight is not null ? "Shh. It's sleepy time." : "NightGuard is on duty";
        Scene.Tagline = s.Streak > 1 ? $"{s.Streak} clean nights in a row. Look at you go! \U0001F525" : "Your nights are in good hands. Sleep tight!";
        Scene.OwlMood = s.CurrentNight is not null ? OwlMood.Sleepy : OwlMood.Awake;

        if (s.CurrentNight is { } cur)
        {
            TonightBig.Text = "Locked \U0001F634";
            TonightText.Text = $"Until {cur.End:dddd} {TimeText.Format(cur.End)}. Go to sleep!";
        }
        else if (s.NextNight is { } n)
        {
            TonightBig.Text = TimeText.Format(n.Start);
            TonightText.Text = (n.Start.Date == DateTime.Today || n.Start.Date == DateTime.Today.AddDays(1) && n.Start.Hour < 12
                ? "Bedtime tonight" : $"Next bedtime: {n.Start:dddd}") + $". Unlocks {TimeText.Format(n.End)}.";
        }
        else
        {
            TonightBig.Text = "Free night \U0001F389";
            TonightText.Text = "No locked night coming up.";
        }

        StreakBig.Text = s.Streak > 0 ? $"\U0001F525 {s.Streak}" : "\U0001F331 0";
        StreakText.Text = s.Streak > 0
            ? $"clean night{(s.Streak == 1 ? "" : "s")} in a row. Best: {Math.Max(s.BestStreak, s.Streak)}."
            : "Your streak starts tonight.";

        var p = Commitment.Progress(s.CommitNights, s.CommitStart, DateTime.Today);
        CommitRing.Fill = Res("NgMoon");
        if (p is null)
        {
            CommitBig.Text = "Not signed yet";
            CommitText.Text = "Make a promise to yourself on the Your why step.";
            CommitRing.Fraction = 0;
            CommitRingText.Text = "?";
        }
        else if (p.Forever)
        {
            CommitBig.Text = "Forever";
            CommitText.Text = $"Night {p.Night}. No finish line, just your new normal.";
            CommitRing.Fill = Res("NgTeal");
            CommitRing.Fraction = 1;
            CommitRingText.Text = "∞";
        }
        else
        {
            CommitBig.Text = p.Done ? "Done! \U0001F389" : $"Night {p.Night} of {p.Total}";
            CommitText.Text = p.Done ? $"You kept your {p.Total}-night promise." : $"{p.Total - p.NightsPassed} to go.";
            CommitRing.Fraction = (double)p.NightsPassed / p.Total;
            CommitRingText.Text = $"{Math.Min(100, p.NightsPassed * 100 / p.Total)}%";
        }
        CommitDoneCard.Visibility = p is { Done: true } ? Visibility.Visible : Visibility.Collapsed;
        if (p is { Done: true })
        {
            CommitDoneTitle.Text = $"You did it! {p.Total} nights. \U0001F389";
            CommitDoneText.Text = "You kept a promise to yourself, which is rarer than it sounds. Ready to go again? Pick a new number, or go all in with forever.";
            if (!_celebratedDone)
            {
                _celebratedDone = true;
                Dispatcher.BeginInvoke(() => Confetti.Burst(Root), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        HomeWhy.Text = string.IsNullOrWhiteSpace(s.Why)
            ? "You haven't written your why yet. It's the most useful thing in here, and it takes a minute."
            : $"“{s.Why}”";
        HomePlan.Text = string.IsNullOrWhiteSpace(s.Plan) ? "" : $"Your plan: when the final call pops up, I will {s.Plan}";
        HomePlan.Visibility = string.IsNullOrWhiteSpace(s.Plan) ? Visibility.Collapsed : Visibility.Visible;

        var end = s.EndDate is { } ed && DateTime.TryParseExact(ed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? $"Last night: {d:dddd d MMMM yyyy} (an end date from an earlier version). After that, NightGuard removes itself.\n" : "";
        var lockText = s.TrustedEnabled ? "Trusted-person mode is on: changes and uninstall need your trusted person's code." : s.UnlockText;
        StatusCardText.Text = end + lockText;
        WelcomeText.Text = "You can always make it stricter and save right away. Making it looser works like quitting anything else: you ask, wait 7 days, and then decide with a clear head.";
    }

    private void EditWhy_Click(object sender, RoutedEventArgs e) => Go(1);

    private void CommitAgain_Click(object sender, RoutedEventArgs e) => StartCommitAgain();

    /// <summary>The commitment is done: open the Your why step, ready to pick and sign a new one.</summary>
    public void StartCommitAgain()
    {
        _recommit = true;
        _signed = false;
        SignButton.IsSigned = false;
        UpdateCommitUi();
        Go(1);
        Dispatcher.BeginInvoke(() => ForeverCard.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Celebrate_Click(object sender, RoutedEventArgs e) => Confetti.Burst(Root);

    // ================================================================ the commitment
    private void CommitChoice_Click(object sender, RoutedEventArgs e)
    {
        var tag = (int)((FrameworkElement)sender).Tag;
        Choose(tag < 0 ? _commitCustom.Value : tag, fromCustom: tag < 0);
    }

    private void Choose(int nights, bool fromCustom)
    {
        if (_signed && nights != _commitChoice) { _signed = false; SignButton.IsSigned = false; }
        _commitChoice = nights;
        _customChosen = fromCustom || (nights != 0 && !Commitment.Choices.Contains(nights));
        UpdateCommitUi();
        OnFormChanged();
    }

    private bool _customChosen;

    private void OnSigned()
    {
        _signed = true;
        UpdateCommitUi();
        OnFormChanged();
        var p = SignButton.TranslatePoint(new Point(SignButton.ActualWidth / 2, 20), Root);
        Confetti.Burst(Root, p);
    }

    private void UpdateCommitUi()
    {
        foreach (var c in _commitCards) c.IsChecked = !_customChosen && _commitChoice == (int)c.Tag;
        ForeverCard.IsChecked = _commitChoice == 0;
        _customCommitCard.IsChecked = _customChosen && _commitChoice is > 0;
        SignButton.IsEnabled = _commitChoice is not null;
        var what = _commitChoice switch { null => "", 0 => "forever", { } n => Commitment.Describe(n) };
        SignButton.Prompt = _commitChoice is null ? "Pick a number above, then sign" : $"Hold to sign: {what}";
        var p = Commitment.Progress(_savedCommit, _savedCommitStart, DateTime.Today);
        var keepsOld = _signed && !_recommit && _commitChoice == _savedCommit && p is not null;
        SignButton.SignedText = keepsOld && p is { } pp
            ? (pp.Forever ? $"Signed: forever. Night {pp.Night} and counting." : pp.Done ? $"Done: {pp.Total} of {pp.Total} nights. Time to commit again?" : $"Signed: night {pp.Night} of {pp.Total}.")
            : $"Signed: {what}. Night 1 starts tonight.";
        SignButton.IsSigned = _signed;
        CommitStateText.Text = _signed && !keepsOld && Configured ? "Saved when you click Save on the last step." : "";
    }

}
