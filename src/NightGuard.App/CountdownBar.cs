using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NightGuard.App;

/// <summary>
/// The little bar the bedtime countdown shrinks into while you save. Its own small window: always on
/// top, never takes the keyboard from your game, and can't be closed (the countdown keeps running).
/// </summary>
internal sealed class CountdownBar : Window
{
    private readonly TextBlock _seconds = new() { FontSize = 30, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _text = new() { FontSize = 14.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, MaxWidth = 260, TextWrapping = TextWrapping.Wrap };
    private readonly Button _actNow;
    private readonly string _actLabel;
    private readonly Func<Task<bool>> _act;
    private readonly DispatcherTimer _onTop = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTime _armedUntil = DateTime.MinValue;
    private double _centerX, _top;
    private bool _closing;

    public CountdownBar(string actLabel, Action expand, Func<Task<bool>> act)
    {
        _actLabel = actLabel;
        _act = act;
        Title = "NightGuard: bedtime";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/NightGuard.ico")); } catch { }

        _seconds.Foreground = (Brush?)Application.Current.TryFindResource("NgMoon") ?? Art.Moon;
        var moon = Art.Crescent(28, Art.Moon);
        moon.Margin = new Thickness(0, 0, 12, 0);
        moon.VerticalAlignment = VerticalAlignment.Center;

        var expandButton = new Button { Content = "⤢  Show", Margin = new Thickness(14, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6), ToolTip = "Show the full countdown" };
        expandButton.SetResourceReference(StyleProperty, "ChunkyButton");
        expandButton.Click += (_, _) => expand();
        _actNow = new Button { Content = actLabel, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        _actNow.SetResourceReference(StyleProperty, "ChunkyButton");
        _actNow.Click += async (_, _) => await ActAsync();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(moon);
        row.Children.Add(_seconds);
        row.Children.Add(_text);
        row.Children.Add(expandButton);
        row.Children.Add(_actNow);
        Content = new Border
        {
            Background = Art.NightSky(), CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 10, 12, 10),
            BorderThickness = new Thickness(2), BorderBrush = (Brush?)Application.Current.TryFindResource("NgMoon") ?? Art.Moon,
            Child = row,
        };

        _onTop.Tick += (_, _) => KeepOnTop();
        SizeChanged += (_, _) => Place();
        Closing += (_, e) => { if (!_closing) e.Cancel = true; };   // Alt+F4 does nothing; the countdown goes on
        Closed += (_, _) => _onTop.Stop();
    }

    /// <summary>Shows the bar centred at <paramref name="centerX"/>, <paramref name="top"/> (device-independent units).</summary>
    public void ShowAt(double centerX, double top)
    {
        _centerX = centerX;
        _top = top;
        if (!IsVisible) Show();
        Place();
        KeepOnTop();
        _onTop.Start();
    }

    public new void Hide()
    {
        _onTop.Stop();
        base.Hide();
    }

    public void CloseForGood()
    {
        _closing = true;
        Close();
    }

    public void Update(int seconds, string actionWord)
    {
        _seconds.Text = seconds.ToString();
        _text.Text = $"seconds until the PC will {actionWord}. Save now! \U0001F4BE";
        if (DateTime.UtcNow > _armedUntil && _actNow.IsEnabled) _actNow.Content = _actLabel;
    }

    private async Task ActAsync()
    {
        if (DateTime.UtcNow > _armedUntil)
        {
            _armedUntil = DateTime.UtcNow.AddSeconds(4);
            _actNow.Content = "Sure? Click again \U0001F449";
            return;
        }
        _armedUntil = DateTime.MinValue;
        _actNow.IsEnabled = false;
        if (!await _act())
        {
            _actNow.IsEnabled = true;
            _actNow.Content = _actLabel;
        }
    }

    private void Place()
    {
        if (ActualWidth <= 0) return;
        Left = _centerX - ActualWidth / 2;
        Top = _top;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private void KeepOnTop()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // topmost, no move/size/activate
    }
}
