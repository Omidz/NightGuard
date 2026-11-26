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

// Builds the repeating cards and rows on each page.
public partial class MainWindow
{
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
}
