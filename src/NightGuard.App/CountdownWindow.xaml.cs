using System.ComponentModel;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NightGuard.App;

/// <summary>
/// The bedtime countdown, built to get through to someone mid-game:
///  - It takes over the whole screen the game is on. If a game in exclusive fullscreen keeps it
///    hidden, the game is minimised (it's about to be shut down anyway).
///  - "Let me save first" shrinks it to a small always-on-top pill so you can save.
///  - With 10 seconds left it takes over the screen again. It can't be dismissed: closing it just
///    turns it back into the pill.
/// It's only a heads-up: the service does the bedtime action when time is up, whatever happens here.
/// </summary>
public partial class CountdownWindow : Window
{
    private const int FinalSeconds = 10;

    private int _left;
    private readonly bool _preview;
    private readonly string _actionWord;
    private bool _pill;
    private bool _done;
    private IntPtr _hwnd;
    private IntPtr _game;            // a fullscreen window that was in front when the countdown started
    private System.Drawing.Rectangle _screen;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _front = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public CountdownWindow(int seconds, string actionWord, string why, bool preview)
    {
        InitializeComponent();
        _left = seconds;
        _preview = preview;
        _actionWord = actionWord;
        Root.Background = Art.NightSky();
        PillPanel.Background = Art.NightSky();
        Art.Stars(Sky, 3840, 2160, 160, 11);
        MoonHolder.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 170, Height = 170, Margin = new Thickness(-35),
            Fill = new RadialGradientBrush(Color.FromArgb(0x55, 0xFF, 0xD6, 0x66), Color.FromArgb(0, 0xFF, 0xD6, 0x66)),
        });
        MoonHolder.Children.Add(Art.Crescent(100, Art.Moon));
        MoonHolder.Children.Add(new Owl { Mood = OwlMood.Sleepy, Width = 96, Height = 104, Margin = new Thickness(-150, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
        var zzz = Art.Zzz(26);
        zzz.Margin = new Thickness(108, -10, 0, 0);
        zzz.HorizontalAlignment = HorizontalAlignment.Left;
        zzz.VerticalAlignment = VerticalAlignment.Top;
        MoonHolder.Children.Add(zzz);
        Prefix.Text = preview ? "PREVIEW (nothing will happen, press Esc to close) \U0001F440" : "It's bedtime. Goodnight! \U0001F634";
        WhyText.Text = string.IsNullOrWhiteSpace(why) ? "" : $"\U0001F4AC You said: “{why}”";
        WhyText.Visibility = string.IsNullOrWhiteSpace(why) ? Visibility.Collapsed : Visibility.Visible;
        UpdateText();

        _tick.Tick += (_, _) => Tick();
        _front.Tick += (_, _) => StayOnTop.Front(this, activate: !_pill);
        SourceInitialized += (_, _) => Setup();
        ContentRendered += (_, _) => Begin();
        Closing += OnClosing;
        KeyDown += (_, e) => { if (_preview && e.Key == System.Windows.Input.Key.Escape) ForceClose(); };
        Closed += (_, _) => { _tick.Stop(); _front.Stop(); };
    }

    /// <summary>Closes it for real (a newer countdown replaces it, or the app is exiting).</summary>
    public void ForceClose()
    {
        _done = true;
        Close();
    }

    private void Setup()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var fg = GetForegroundWindow();
        if (IsFullscreenApp(fg)) _game = fg;
        _screen = System.Windows.Forms.Screen.FromHandle(fg != IntPtr.Zero ? fg : _hwnd).Bounds;
        ShowFull();
    }

    private void Begin()
    {
        SystemSounds.Exclamation.Play();
        _tick.Start();
        _front.Start();
        BreakThrough();
    }

    /// <summary>A game in exclusive fullscreen can keep any window hidden. If we're not in front, minimise it.</summary>
    private void BreakThrough()
    {
        StayOnTop.Front(this, activate: true);
        if (_preview || _game == IntPtr.Zero) return;
        var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        check.Tick += (_, _) =>
        {
            check.Stop();
            if (GetForegroundWindow() != _hwnd && IsWindow(_game) && IsFullscreenApp(_game))
            {
                ShowWindow(_game, 6 /* SW_MINIMIZE */);
                StayOnTop.Front(this, activate: true);
            }
        };
        check.Start();
    }

    private void Tick()
    {
        _left--;
        if (_left <= 0)
        {
            ForceClose();
            return;
        }
        UpdateText();
        if (_pill && _left == FinalSeconds)
        {
            // Last call: take over the screen again
            ShowFull();
            SystemSounds.Exclamation.Play();
            BreakThrough();
        }
    }

    private void UpdateText()
    {
        Seconds.Text = _left.ToString();
        PillSeconds.Text = _left.ToString();
        Line.Text = $"This PC will {_actionWord} in {_left} second{(_left == 1 ? "" : "s")}.";
        PillText.Text = $"seconds until the PC will {_actionWord}. Save now! \U0001F4BE";
        var canShrink = _left > FinalSeconds;
        SaveFirstButton.Visibility = canShrink ? Visibility.Visible : Visibility.Collapsed;
        SmallPrint.Text = canShrink
            ? $"Need to save? Shrink this to a little bar on top. It comes back {FinalSeconds} seconds before the end."
            : "Closing this window doesn't stop it. Sweet dreams! \U0001F319";
    }

    private void ShowFull()
    {
        _pill = false;
        FullPanel.Visibility = Visibility.Visible;
        PillPanel.Visibility = Visibility.Collapsed;
        SetWindowPos(_hwnd, new IntPtr(-1), _screen.X, _screen.Y, _screen.Width, _screen.Height, 0x0040 /* SWP_SHOWWINDOW */);
    }

    private void ShowPill()
    {
        _pill = true;
        FullPanel.Visibility = Visibility.Collapsed;
        PillPanel.Visibility = Visibility.Visible;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var w = (int)(560 * dpi);
        var h = (int)(76 * dpi);
        SetWindowPos(_hwnd, new IntPtr(-1), _screen.X + (_screen.Width - w) / 2, _screen.Y + (int)(12 * dpi), w, h,
                     0x0040 | 0x0010 /* SWP_SHOWWINDOW | SWP_NOACTIVATE */);
    }

    private void SaveFirst_Click(object sender, RoutedEventArgs e) => ShowPill();

    private void Expand_Click(object sender, RoutedEventArgs e) => ShowFull();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_done || _preview) return;
        // Can't be dismissed, only shrunk (and only until the last 10 seconds)
        e.Cancel = true;
        if (_left > FinalSeconds) ShowPill();
    }

    // ---- Win32 ----
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Does this window cover its whole monitor (a fullscreen game or video), and isn't the desktop?</summary>
    private static bool IsFullscreenApp(IntPtr w)
    {
        if (w == IntPtr.Zero || w == GetShellWindow() || !GetWindowRect(w, out var r)) return false;
        var sb = new StringBuilder(64);
        GetClassNameW(w, sb, sb.Capacity);
        var cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var b = System.Windows.Forms.Screen.FromHandle(w).Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }
}
