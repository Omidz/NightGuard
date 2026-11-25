using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NightGuard.Core.Ipc;
using NightGuard.Core.Journal;

namespace NightGuard.App;

/// <summary>
/// On the first sign-in each day: a cheer (or a gentle roast) for last night, the streak of clean
/// nights, badges, and an optional mood + note for the private journal.
/// </summary>
public partial class CheckInWindow : Window
{
    private static readonly Random Rng = new();
    private int _mood;
    private readonly Badge? _badge;

    public CheckInWindow(CheckInInfo info)
    {
        InitializeComponent();
        static string Pick(params string[] items) => items[Rng.Next(items.Length)];
        string title, body;
        switch (info.Kind)
        {
            case "Clean":
                title = Pick("Good morning, legend!", "Look at you!", "Another night, another win!", "Well rested? We think so.");
                body = Pick("You were off before bedtime last night. Sleep: 1, One More Game: 0.",
                            "Last night you logged off like a responsible adult. Your pillow is proud of you.",
                            "No bouncer needed last night. You did that all by yourself.",
                            "You beat the 'just one more' monster last night. Respect.");
                break;
            case "SteppedIn":
                title = Pick("Morning! Rough night?", "Hey, it happens.", "New day, clean slate.");
                body = Pick("NightGuard had to tuck you in last night. No judgment. Well, a little. Tonight's a fresh start.",
                            "The bouncer had to step in last night. That's what it's here for. Try to beat it to bedtime tonight.",
                            "Last night went into overtime. Streak resets, but you're still here, and that counts.");
                break;
            default:
                title = "Good morning!";
                body = "Last night was a free night. Hope it was a good one. Back to the routine tonight!";
                break;
        }
        var streak = info.Streak switch
        {
            0 => "Streak: 0. Tonight is night one.",
            1 => "\U0001F525 1 clean night. Every streak starts somewhere.",
            _ => $"\U0001F525 {info.Streak} clean nights in a row!" + (info.Streak >= 7 ? " That is a habit now." : ""),
        };
        // A new badge, the first time you reach it (the streak is at a milestone and is your best ever)
        if (info.Kind == "Clean" && info.Streak == info.Best) _badge = Badges.At(info.Streak);
        if (_badge is not null) { title = $"New badge: {_badge.Name}!"; body = _badge.Line + "\n" + body; }
        if (Badges.Next(Math.Max(info.Best, info.Streak)) is { } next) streak += $"  Next badge: {next.Name} at {next.Nights}.";

        TitleText.Text = title;
        Body.Text = body;
        StreakText.Text = streak;
        WhyText.Text = string.IsNullOrWhiteSpace(info.Why) ? "" : $"Remember why you started: “{info.Why}”";
        WhyText.Visibility = string.IsNullOrWhiteSpace(info.Why) ? Visibility.Collapsed : Visibility.Visible;

        string[] faces = ["\U0001F62B", "\U0001F615", "\U0001F610", "\U0001F642", "\U0001F601"];
        for (var m = 1; m <= 5; m++)
        {
            var b = new MoodButton(faces[m - 1], Badges.MoodNames[m], m);
            b.Click += (_, _) =>
            {
                _mood = (int)b.Tag;
                foreach (var x in Moods.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()) x.IsChecked = (int)x.Tag == _mood;
            };
            Moods.Children.Add(b);
        }

        // Close by itself after 10 minutes if nobody's there
        var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        t.Tick += (_, _) => { t.Stop(); Close(); };
        t.Start();
        Closed += (_, _) => t.Stop();
        Topmost = true;
    }

    private sealed class MoodButton : System.Windows.Controls.Primitives.ToggleButton
    {
        public MoodButton(string face, string name, int mood)
        {
            Tag = mood;
            Margin = new Thickness(0, 0, 8, 0);
            Padding = new Thickness(4, 6, 4, 6);
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = face, FontSize = 24, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"), HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center });
            Content = sp;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_mood > 0 || Note.Text.Trim().Length > 0)
        {
            var r = await ServiceClient.SendAsync(new IpcRequest
            {
                Op = Ops.AddJournal,
                Entry = new JournalEntry { Mood = _mood, Note = Note.Text.Trim(), Badge = _badge?.Name ?? "" },
            });
            if (!r.Ok) { Ask.Warn(this, r.Error ?? "Couldn't save."); return; }
        }
        Close();
    }
}
