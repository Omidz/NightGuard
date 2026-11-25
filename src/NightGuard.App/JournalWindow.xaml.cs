using System.IO;
using System.Windows;
using NightGuard.Core.Ipc;
using NightGuard.Core.Journal;

namespace NightGuard.App;

/// <summary>Your why, streak, badges and check-in entries. Each Windows account sees only its own journal.</summary>
public partial class JournalWindow : Window
{
    private sealed record Row(string When, string Mood, string LastNight, string Streak, string Badge, string Note);

    private static readonly Dictionary<string, string> NightWords = new()
    {
        ["Clean"] = "clean", ["SteppedIn"] = "bouncer stepped in", ["Free"] = "free night",
    };

    private List<JournalEntry> _entries = [];

    public JournalWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var j = await ServiceClient.SendAsync(Ops.Journal);
        var s = (await ServiceClient.SendAsync(Ops.Status)).Status;
        if (!j.Ok) { Summary.Text = j.Error; return; }
        _entries = (j.Journal ?? []).OrderByDescending(e => e.Date + " " + e.Time, StringComparer.Ordinal).ToList();
        Entries.ItemsSource = _entries.Select(e => new Row(
            $"{e.Date} {e.Time}",
            e.Mood > 0 ? $"{e.Mood} {Badges.MoodNames[e.Mood]}" : "",
            NightWords.TryGetValue(e.LastNight, out var w) ? w : "",
            e.Streak.ToString(), e.Badge, e.Note)).ToList();
        EmptyText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.IsEnabled = _entries.Count > 0;

        var moods = _entries.Take(7).Where(e => e.Mood > 0).Select(e => e.Mood).ToList();
        var avg = moods.Count > 0 ? $"average mood lately: {moods.Average():0.0} out of 5" : "no moods logged yet";
        var streak = s is { Configured: true } ? $"Current streak: {s.Streak} clean night(s)" : "Streak: starts after setup";
        Summary.Text = $"{streak}   |   {_entries.Count} entries   |   {avg}";
        var best = Math.Max(s?.BestStreak ?? 0, s?.Streak ?? 0);
        var earned = Badges.Earned(best);
        var badgeText = earned.Count > 0
            ? $"Badges ({earned.Count} of {Badges.All.Length}): {string.Join(", ", earned.Select(b => b.Name))}"
            : "Badges: none yet. First one after your first clean night!";
        badgeText += Badges.Next(best) is { } next
            ? $"\nNext: {next.Name} at {next.Nights} clean nights in a row (best so far: {best})."
            : "\nAll badges earned. A full year. You absolute legend.";
        BadgeText.Text = badgeText;
        WhyText.Text = string.IsNullOrWhiteSpace(s?.Why)
            ? "Why I started: not written yet. Add it on the \"Your why and journal\" step. Highly recommended!"
            : $"Why I started: “{s.Why}”";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt",
            FileName = $"NightGuard journal {DateTime.Now:yyyy-MM-dd}.txt",
        };
        if (dlg.ShowDialog(this) != true) return;
        var lines = _entries.Select(x =>
            $"{x.Date} {x.Time}  mood: {(x.Mood > 0 ? x.Mood + "/5" : "-")}  last night: {(NightWords.TryGetValue(x.LastNight, out var w) ? w : "")}  streak: {x.Streak}  {x.Badge}  {x.Note}");
        try
        {
            File.WriteAllLines(dlg.FileName, lines);
            Ask.Info(this, "Saved.");
        }
        catch (Exception ex) { Ask.Error(this, ex.Message); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
