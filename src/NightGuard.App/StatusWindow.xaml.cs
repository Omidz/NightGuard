using System.Text;
using System.Windows;
using NightGuard.Core.Ipc;

namespace NightGuard.App;

public partial class StatusWindow : Window
{
    public StatusWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var r = await ServiceClient.SendAsync(Ops.Status);
        var log = await ServiceClient.SendAsync(Ops.Log);
        Text.Text = r.Ok && r.Status is { } s ? Describe(s, log.Lines ?? []) : r.Error ?? "";
        Text.ScrollToEnd();
    }

    public static string Describe(StatusInfo s, List<string> log)
    {
        var sb = new StringBuilder();
        void Add(string label, string value) => sb.AppendLine($"{label,-16}: {value}");
        if (!s.Configured)
        {
            sb.AppendLine("NIGHTGUARD IS INSTALLED BUT NOT SET UP YET");
            if (s.SafetyStandDown is { } p) sb.AppendLine($"Settings problem: {p}");
        }
        else
        {
            sb.AppendLine(s.SafetyStandDown is null ? "NIGHTGUARD IS ACTIVE" : $"NIGHTGUARD IS STANDING DOWN: {s.SafetyStandDown}");
            sb.AppendLine();
            if (s.CurrentNight is { } cur) Add("Locked now", $"until {cur.End:ddd} {TimeText.Format(cur.End)}");
            Add("Next night", s.NextNight is { } n ? $"{n.Start:ddd} {TimeText.Format(n.Start)} until {n.End:ddd} {TimeText.Format(n.End)}" : "none scheduled");
            Add("At bedtime", $"{s.Action}, after a {s.CountdownSeconds} s countdown, repeats every {s.RecheckMinutes} min");
            Add("End date", s.Expiry is { } x ? $"last night {s.EndDate}, removes itself {x:ddd yyyy-MM-dd} {TimeText.Format(x)}" : "none, runs until uninstalled");
            Add("Night mode", s.NightModeReason is not null ? "ON, settings and uninstall are locked right now" : $"off (starts {s.NightModeMinutes} min before bedtime)");
            Add("Self-lock", s.TrustedEnabled ? "trusted-person code skips the cool-off" : s.UnlockText);
            Add("Trusted person", s.TrustedEnabled ? "ON, changes need the code" + (s.TrustedContact.Length > 0 ? $" ({s.TrustedContact})" : "") : "off");
            Add("Blocking", s.BlockingEnabled ? $"{s.BlockedSites} sites, {s.BlockedApps} apps/folders, {(s.BlockingActive ? "BLOCKING NOW" : "not right now")}" : "off");
            Add("Streak", $"{s.Streak} clean night(s), best {s.BestStreak}");
            Add("Time zone", s.TimeZoneName + (s.TimeZoneDiffersFromPc ? " (the PC's time zone has changed since saving; save your settings to switch)" : ""));
            if (s.ScreenTimeEnabled) { sb.AppendLine(); sb.AppendLine(s.UsageText); }
        }
        sb.AppendLine();
        Add("Service", $"running, version {s.ServiceVersion}");
        Add("Hibernate", s.HibernateAvailable ? "available" : "off in Windows (Hibernate falls back to Shutdown)");
        if (log.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent activity:");
            foreach (var l in log.TakeLast(60)) sb.AppendLine("  " + l);
        }
        return sb.ToString();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
