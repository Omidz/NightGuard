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

    // ================================================================ building the pages
    private void BuildHowItWorks()
    {
        (string Emoji, string Title, string Text)[] items =
        [
            ("💬", "Your why", "One sentence, one plan, and a promise you sign. This is the part that makes it stick."),
            ("🛏️", "Your nights", "Pick bedtimes and when the PC unlocks in the morning."),
            ("⏰", "Nudges, then lights out", "Friendly warnings, a countdown, then the PC hibernates. No arguing."),
            ("✨", "Extras", "Game budgets, site blocking, and a trusted person who holds the key. All optional."),
        ];
        foreach (var (emoji, title, text) in items)
        {
            var sp = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var badge = Badge(emoji);
            DockPanel.SetDock(badge, Dock.Left);
            head.Children.Add(badge);
            var h = Text(title, "H2");
            h.VerticalAlignment = VerticalAlignment.Center;
            h.Margin = new Thickness(0);
            head.Children.Add(h);
            sp.Children.Add(head);
            sp.Children.Add(Text(text, "Hint"));
            HowItWorks.Children.Add(Card(sp, 236));
        }
    }

    private void BuildRitual()
    {
        (string N, string Title, string Text)[] steps =
        [
            ("1", "Your why", "One sentence about what you'll gain, in your own words. Not \"be healthier\". Something that hits."),
            ("2", "Your plan", "Finish the line \"When the final call pops up, I will...\". Small, physical, doable at 11:55 PM."),
            ("3", "Each morning", "Two minutes: how you feel, and three good things from yesterday. Read it back on the hard nights."),
        ];
        foreach (var (n, title, text) in steps)
        {
            var sp = new DockPanel { Width = 236, Margin = new Thickness(0, 0, 16, 12) };
            var num = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = n, FontWeight = FontWeights.Bold, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("NgOnMoon") },
                Background = Res("NgMoon"),
            };
            DockPanel.SetDock(num, Dock.Left);
            sp.Children.Add(num);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = title, FontFamily = (FontFamily)FindResource("NgSerif"), FontSize = 21, Foreground = Res("NgText") });
            body.Children.Add(Text(text, "Hint"));
            sp.Children.Add(body);
            RitualSteps.Children.Add(sp);
        }
        foreach (var idea in WhyExamples) AddIdea(WhyIdeas, idea, WhyBox);
        foreach (var idea in PlanExamples) AddIdea(PlanIdeas, idea, PlanBox);
    }

    private static void AddIdea(WrapPanel host, string idea, TextBox target)
    {
        var chip = new Button { Content = idea };
        chip.SetResourceReference(StyleProperty, "Chip");
        chip.Click += (_, _) => { target.Text = idea; target.Focus(); target.CaretIndex = target.Text.Length; };
        host.Children.Add(chip);
    }

    private void BuildCommitCards()
    {
        (int Nights, string Caption)[] choices =
        [
            (Commitment.Choices[0], "A first week. Prove it to yourself."),
            (Commitment.Choices[1], "A month. Long enough to feel it."),
            (Commitment.Choices[2], "Habit-forming. 66 days is the average time a new habit takes to stick (Lally and colleagues, 2010)."),
        ];
        foreach (var (nights, caption) in choices)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = nights.ToString(CultureInfo.CurrentCulture), FontSize = 44, FontWeight = FontWeights.Bold, FontFamily = (FontFamily)FindResource("NgDisplay"), Foreground = Res("NgMoon") });
            sp.Children.Add(new TextBlock { Text = "nights", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, -4, 0, 6) });
            sp.Children.Add(Text(caption, "Hint"));
            var card = new ToggleButton { Content = sp, Tag = nights, Width = 178 };
            card.SetResourceReference(StyleProperty, "ChoiceCard");
            card.Click += CommitChoice_Click;
            _commitCards.Add(card);
            CommitChoices.Children.Add(card);
        }
        var custom = new StackPanel();
        custom.Children.Add(new TextBlock { Text = "Your number", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 12) });
        _commitCustom.Value = 14;
        custom.Children.Add(_commitCustom);
        custom.Children.Add(Text("Anything from 1 night to 10 years.", "Hint"));
        _customCommitCard.Content = custom;
        _customCommitCard.Tag = -1;
        _customCommitCard.Width = 216;
        _customCommitCard.SetResourceReference(StyleProperty, "ChoiceCard");
        _customCommitCard.Click += CommitChoice_Click;
        _commitCustom.ValueChanged += (_, _) =>
        {
            // Only when someone is actually changing the number
            if (_loading || !_commitCustom.IsKeyboardFocusWithin) return;
            Choose(_commitCustom.Value, fromCustom: true);
        };
        CommitChoices.Children.Add(_customCommitCard);
    }

    private void BuildNightCards()
    {
        for (var i = 0; i < 7; i++)
        {
            var on = new CheckBox { Content = Defaults.DayNames[i] + " night", FontWeight = FontWeights.SemiBold, FontSize = 15 };
            on.SetResourceReference(StyleProperty, "ToggleSwitch");
            var bed = new TimePicker();
            var unlock = new TimePicker();
            var note = Text("", "Hint");
            var before = new ColumnDefinition();
            var locked = new ColumnDefinition();
            var after = new ColumnDefinition();
            var bar = new Grid { Height = 10, Margin = new Thickness(0, 14, 0, 2) };
            bar.ColumnDefinitions.Add(before);
            bar.ColumnDefinitions.Add(locked);
            bar.ColumnDefinitions.Add(after);
            var track = new Border { CornerRadius = new CornerRadius(5), Background = Res("NgSurface2") };
            Grid.SetColumnSpan(track, 3);
            bar.Children.Add(track);
            var fill = new Border { CornerRadius = new CornerRadius(5), Background = Res("NgMoon") };
            Grid.SetColumn(fill, 1);
            bar.Children.Add(fill);
            var scale = new DockPanel();
            var left = Text("6 PM", "Hint", 11);
            DockPanel.SetDock(left, Dock.Left);
            var right = Text("noon", "Hint", 11);
            DockPanel.SetDock(right, Dock.Right);
            scale.Children.Add(left);
            scale.Children.Add(right);
            scale.Children.Add(new TextBlock { Text = "midnight", FontSize = 11, Foreground = Res("NgMuted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });

            var sp = new StackPanel();
            sp.Children.Add(on);
            sp.Children.Add(Text("Bedtime", "Hint"));
            sp.Children[^1].SetValue(MarginProperty, new Thickness(0, 14, 0, 4));
            sp.Children.Add(bed);
            sp.Children.Add(Text("Unlocks at", "Hint"));
            sp.Children[^1].SetValue(MarginProperty, new Thickness(0, 10, 0, 4));
            sp.Children.Add(unlock);
            sp.Children.Add(bar);
            sp.Children.Add(scale);
            sp.Children.Add(note);
            bed.ValueChanged += (_, _) => OnFormChanged();
            unlock.ValueChanged += (_, _) => OnFormChanged();
            NightsPanel.Children.Add(Card(sp, 274));
            _nights.Add(new NightCard(on, bed, unlock, note, before, locked, after, fill));
        }
    }

    private void BuildWarningCards()
    {
        foreach (var p in Defaults.WarningPresets)
        {
            var on = new CheckBox();
            on.SetResourceReference(StyleProperty, "ToggleSwitch");
            System.Windows.Automation.AutomationProperties.SetName(on, p.Name);
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(on, Dock.Right);
            head.Children.Add(on);
            var badge = Badge(p.Emoji);
            DockPanel.SetDock(badge, Dock.Left);
            head.Children.Add(badge);
            var title = Text(p.Name, "H2");
            title.VerticalAlignment = VerticalAlignment.Center;
            title.Margin = new Thickness(0);
            head.Children.Add(title);

            var minutes = new PillPicker();
            minutes.SetChoices(p.Minutes.Select(m => (m, $"{m} min")), m => $"{m} min");
            minutes.Value = p.DefaultMinutes;
            var sound = new CheckBox { Content = "With a sound", IsChecked = true };
            sound.SetResourceReference(StyleProperty, "ToggleSwitch");
            var custom = new TextBox { MaxLength = 200, TextWrapping = TextWrapping.Wrap, MinHeight = 56, AcceptsReturn = false };
            var preview = new Button { Content = "👀  Preview", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
            preview.SetResourceReference(StyleProperty, "ChunkyButton");
            var body = new StackPanel();
            body.Children.Add(Text("Pops up before bedtime", "Label"));
            body.Children.Add(minutes);
            body.Children.Add(sound);
            sound.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(Text("Your own words (optional)", "Label"));
            body.Children[^1].SetValue(MarginProperty, new Thickness(0, 16, 0, 6));
            body.Children.Add(custom);
            body.Children.Add(preview);

            var sp = new StackPanel();
            sp.Children.Add(head);
            sp.Children.Add(Text(p.Blurb, "Hint"));
            sp.Children[^1].SetValue(MarginProperty, new Thickness(0, 0, 0, 14));
            sp.Children.Add(body);
            var card = new WarnCard(p, on, minutes, sound, custom, preview, body);
            preview.Click += (_, _) => PreviewWarning(card);
            _warns.Add(card);
            WarningsPanel.Children.Add(Card(sp, 360));
        }
    }

    private void BuildActionCards()
    {
        foreach (var a in ActionOrder)
        {
            var (emoji, name, strength, text) = ActionInfo[a];
            var sp = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 30, 8) };
            head.Children.Add(Art.Emoji(emoji, 26, Brushes.White));
            var nameText = Text(name, "H2");
            nameText.Margin = new Thickness(10, 0, 0, 0);
            nameText.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(nameText);
            sp.Children.Add(head);
            if (a == "Shutdown")
            {
                sp.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8), Background = Art.Alarm(), Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8),
                    Child = new TextBlock { Text = "BRAVEST 🦁 STRONGEST", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
                });
            }
            if (a == "Hibernate")
            {
                sp.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8), Background = Res("NgMoon"), Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8),
                    Child = new TextBlock { Text = "RECOMMENDED", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Res("NgOnMoon") },
                });
            }
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            dots.Children.Add(new TextBlock { Text = "Strength ", FontSize = 12, Foreground = Res("NgMuted"), VerticalAlignment = VerticalAlignment.Center });
            for (var i = 0; i < 4; i++)
                dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Margin = new Thickness(3, 0, 0, 0),
                    Fill = i >= strength ? Res("NgSurface2") : a == "Shutdown" ? Res("NgRed") : Res("NgMoon") });
            sp.Children.Add(dots);
            sp.Children.Add(Text(text, "Hint"));
            var lockNote = new TextBlock
            {
                Text = "🔒 Weaker than what you have now: needs the 7-day cool-off (or the trusted person's code).",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Res("NgOrange"), Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed,
            };
            sp.Children.Add(lockNote);
            _actionLocks[a] = lockNote;
            var card = new ToggleButton { Content = sp, Tag = a, Width = 236 };
            card.SetResourceReference(StyleProperty, "ChoiceCard");
            card.Click += (_, _) => SelectAction(a);
            _actionCards[a] = card;
            ActionsPanel.Children.Add(card);
        }
    }

    private string _action = "Hibernate";

    /// <summary>
    /// Once NightGuard runs, the bedtime action can move to the same or a stronger one any time.
    /// Weaker ones are locked until an unlock (cool-off) is open, or the trusted person's code is used.
    /// </summary>
    private void UpdateActionLocks()
    {
        var current = _savedAction is null ? 0 : NightGuard.Core.SelfLock.SelfLockRules.ActionStrength(_savedAction);
        var canLoosen = !Configured || TrustedOn || _status?.UnlockState == "Open";
        foreach (var (a, card) in _actionCards)
        {
            var locked = !canLoosen && NightGuard.Core.SelfLock.SelfLockRules.ActionStrength(a) < current;
            card.IsEnabled = !locked;
            card.ToolTip = locked ? "Weaker than your current bedtime action. Request an unlock to choose it." : null;
            _actionLocks[a].Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SelectAction(string a)
    {
        _action = Defaults.Actions.Contains(a) ? a : "Hibernate";
        foreach (var (k, c) in _actionCards) c.IsChecked = k == _action;
    }

    private void BuildSlotCards()
    {
        for (var i = 0; i < 4; i++)
        {
            var on = new CheckBox { Content = $"Time slot {i + 1}", FontWeight = FontWeights.SemiBold };
            on.SetResourceReference(StyleProperty, "ToggleSwitch");
            var days = new ToggleButton[7];
            var dayRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) };
            for (var d = 0; d < 7; d++)
            {
                days[d] = new ToggleButton { Content = Defaults.Days[d][..2], IsChecked = d < 5, MinWidth = 38, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 4, 4), FontSize = 12.5 };
                days[d].SetResourceReference(StyleProperty, "Pill");
                days[d].MinWidth = 38;
                days[d].Padding = new Thickness(6, 5, 6, 5);
                days[d].Margin = new Thickness(0, 0, 4, 4);
                dayRow.Children.Add(days[d]);
            }
            var from = new TimePicker();
            var to = new TimePicker();
            from.Value = "09:00";
            to.Value = "17:00";
            from.ValueChanged += (_, _) => OnFormChanged();
            to.ValueChanged += (_, _) => OnFormChanged();
            var body = new StackPanel();
            body.Children.Add(dayRow);
            body.Children.Add(Text("From", "Hint"));
            body.Children.Add(from);
            body.Children.Add(Text("To", "Hint"));
            body.Children[^1].SetValue(MarginProperty, new Thickness(0, 8, 0, 4));
            body.Children.Add(to);
            var sp = new StackPanel();
            sp.Children.Add(on);
            sp.Children.Add(body);
            SlotsPanel.Children.Add(Card(sp, 330));
            _slots.Add(new SlotCard(on, days, from, to, body));
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

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

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
        cfg.ShortcutStartMenu = true;
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
            r.Note.Text = text;
            if (hours is null or > 16) r.Note.Foreground = Res("NgRed"); else r.Note.ClearValue(TextBlock.ForegroundProperty);
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

        var errs = ConfigValidator.Validate(ReadForm());
        if (errs.Count > 0)
        {
            BottomHint.Foreground = Res("NgRed");
            BottomHint.Text = "Fix before saving: " + errs[0] + (errs.Count > 1 ? $" (+{errs.Count - 1} more)" : "");
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

    // ================================================================ previews
    private void PreviewWarning(WarnCard card)
    {
        var cfg = ReadForm();
        var now = DateTime.Now;
        var night = ScheduleMath.NextNight(cfg, now) ?? ScheduleMath.GetNight(cfg, now, ignoreEndDate: true)
                    ?? new Night("", now.Date, now.Date.AddDays(1), now.Date.AddDays(1).AddHours(6));
        var minutes = card.Minutes.Value;
        var text = ScheduleMath.FormatMessage(Compose(card.Preset, card.Custom.Text), cfg, night, minutes, "t");
        var final = _warns.Where(w => w.On.IsChecked == true).Select(w => w.Minutes.Value).DefaultIfEmpty(minutes).Min();
        if (minutes <= final && cfg.Why.Length > 0) text += $"\n\nRemember why you started:\n“{cfg.Why}”";
        if (minutes <= final && cfg.Plan.Length > 0) text += $"\n\nYour plan: {cfg.Plan}";
        var urgent = minutes <= 5;
        if (card.Sound.IsChecked == true) System.Media.SystemSounds.Exclamation.Play();
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

    private void SocialPreset_Click(object sender, RoutedEventArgs e) => SitesEditor.AddRange(Defaults.SocialPreset);

    private static string? ProtectedProblem(string path, string what) =>
        PathRules.ForThisPc().IsProtected(path) ? $"That {what} is part of Windows, so it can't be used here." : null;

    private string? PickProgram()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick a program to block", Filter = "Programs (*.exe)|*.exe" };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private string? PickFolder(string title)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }

    /// <summary>Sets up the list editors: what each accepts, and their browse buttons.</summary>
    private void SetupListEditors()
    {
        SitesEditor.Normalize = raw =>
        {
            // Accept pasted links: https://www.reddit.com/r/... becomes reddit.com
            var d = raw.Trim().ToLowerInvariant();
            if (d.Contains("://", StringComparison.Ordinal)) d = d[(d.IndexOf("://", StringComparison.Ordinal) + 3)..];
            d = d.Split('/', '?', '#')[0];
            return d.StartsWith("www.", StringComparison.Ordinal) ? d[4..] : d;
        };
        SitesEditor.Validate = d =>
            !DomainRules.IsValid(d) ? "That isn't a site name. Try something like reddit.com."
            : DomainRules.IsProtected(d) ? "Windows needs that site, so it can't be blocked."
            : null;

        AppsEditor.Validate = p => ProtectedProblem(p, File.Exists(p) ? "program" : "folder");
        AppsEditor.AddBrowseButton("\U0001F4E6  Add a program...", PickProgram);
        AppsEditor.AddBrowseButton("\U0001F4C1  Add a whole folder...", () => PickFolder("Every program in this folder gets blocked (for example a Steam or Discord folder)"));

        GameFoldersEditor.Validate = p => !PathRules.IsFullPath(p) ? "Use a full folder path, like D:\\Games." : ProtectedProblem(p, "folder");
        GameFoldersEditor.AddBrowseButton("\U0001F4C1  Browse for a game folder...", () => PickFolder("Pick a folder where games are installed"));

        static string? ProgramName(string raw)
        {
            var n = raw.Trim().Trim('"');
            if (n.Contains('\\', StringComparison.Ordinal)) n = Path.GetFileName(n);
            return ConfigSerializer.StripExe(n);
        }
        foreach (var editor in new[] { GameProcsEditor, IgnoreProcsEditor, LauncherProcsEditor })
        {
            editor.Normalize = ProgramName;
            editor.Validate = n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^[\w .+()\-]{1,100}$") ? null : "That isn't a program name (as in Task Manager, without .exe).";
        }
        foreach (var editor in new[] { SitesEditor, AppsEditor, GameFoldersEditor, GameProcsEditor, IgnoreProcsEditor, LauncherProcsEditor })
            editor.Changed += (_, _) => OnFormChanged();
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
            TrustState.Foreground = Res("NgGreen");
        }
        else if (TrustCheck.IsChecked == true)
        {
            TrustState.Text = "Your trusted person sets the code when you click " + SaveButton.Content + " on the last step, so have them next to you then.";
            TrustState.Foreground = Res("NgMoon");
        }
        else
        {
            TrustState.Text = "Off. Anyone with an administrator account on this PC can change the settings (within the self-lock rules).";
            TrustState.Foreground = Res("NgMuted");
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
