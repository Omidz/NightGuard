using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NightGuard.App;

/// <summary>
/// Popups that get through to someone in a game:
///  - always-on-top, pulled to the foreground (with the usual ALT-key trick, since Windows blocks
///    background apps from stealing focus), and re-asserted every second so a game can't bury it;
///  - taking focus also knocks most exclusive-fullscreen games back to the desktop.
/// </summary>
internal static class StayOnTop
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint NoSize = 0x1, NoMove = 0x2, NoActivate = 0x10, ShowWin = 0x40;

    public static void Front(Window w, bool activate)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            if (!activate)
            {
                SetWindowPos(h, HwndTopmost, 0, 0, 0, 0, NoSize | NoMove | NoActivate);
                return;
            }
            SetWindowPos(h, HwndTopmost, 0, 0, 0, 0, NoSize | NoMove | ShowWin);
            if (GetForegroundWindow() != h)
            {
                keybd_event(0x12, 0, 0, UIntPtr.Zero);   // tap ALT
                keybd_event(0x12, 0, 2, UIntPtr.Zero);
                ShowWindow(h, 9);                        // restore
                BringWindowToTop(h);
                SetForegroundWindow(h);
            }
        }
        catch { }
    }

    /// <summary>Grabs focus for the first <paramref name="grabSeconds"/> seconds, then just stays above other windows.</summary>
    public static void Enable(Window w, int grabSeconds = 5)
    {
        w.Topmost = true;
        var ticks = 0;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += (_, _) => Front(w, ++ticks <= grabSeconds);
        w.ContentRendered += (_, _) => { Front(w, true); t.Start(); };
        w.Closed += (_, _) => t.Stop();
    }
}

internal static class TimeText
{
    /// <summary>Does Windows show times as 12-hour (AM/PM) on this PC?</summary>
    public static bool Is12Hour => CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t', StringComparison.Ordinal);

    public static string Format(DateTime t) => t.ToString(Is12Hour ? "h:mm tt" : "HH:mm", CultureInfo.CurrentCulture);

    public static string Format(string hhmm) =>
        NightGuard.Core.Schedule.ScheduleMath.IsTime(hhmm) ? Format(DateTime.Today + NightGuard.Core.Schedule.ScheduleMath.ParseTime(hhmm)) : hhmm;

    public static string Hint => Is12Hour
        ? "Times use the 12-hour clock (AM / PM), same as your Windows clock."
        : "Times use the 24-hour clock (00:00 is midnight), same as your Windows clock.";
}

internal static class Ask
{
    public static void Info(Window? owner, string text) => Show(owner, text, MessageBoxButton.OK, MessageBoxImage.Information);
    public static void Warn(Window? owner, string text) => Show(owner, text, MessageBoxButton.OK, MessageBoxImage.Warning);
    public static void Error(Window? owner, string text) => Show(owner, text, MessageBoxButton.OK, MessageBoxImage.Error);

    public static bool YesNo(Window? owner, string text) =>
        Show(owner, text, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>The final "are you sure", with No as the default.</summary>
    public static bool Sure(Window? owner, string text) =>
        (owner is null
            ? MessageBox.Show(text, "NightGuard - are you sure?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(owner, text, "NightGuard - are you sure?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No))
        == MessageBoxResult.Yes;

    private static MessageBoxResult Show(Window? owner, string text, MessageBoxButton buttons, MessageBoxImage icon) =>
        owner is null ? MessageBox.Show(text, "NightGuard", buttons, icon) : MessageBox.Show(owner, text, "NightGuard", buttons, icon);
}
