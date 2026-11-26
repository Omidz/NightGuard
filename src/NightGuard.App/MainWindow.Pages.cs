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

// Handlers for the individual pages: bedtimes, previews, screen time, blocking, trusted person.
public partial class MainWindow
{
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
}
