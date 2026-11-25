using System.Media;
using System.Windows;
using System.Windows.Threading;

namespace NightGuard.App;

/// <summary>The bedtime countdown. Purely a heads-up: the service does the action when time is up.</summary>
public partial class CountdownWindow : Window
{
    private int _left;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public CountdownWindow(int seconds, string actionWord, string why, bool preview)
    {
        InitializeComponent();
        _left = seconds;
        Prefix.Text = preview ? "PREVIEW (nothing will happen)" : "It's bedtime.";
        Line.Text = $"This PC will {actionWord} in {seconds} seconds.";
        WhyText.Text = string.IsNullOrWhiteSpace(why) ? "" : $"You said: “{why}”";
        WhyText.Visibility = string.IsNullOrWhiteSpace(why) ? Visibility.Collapsed : Visibility.Visible;
        Seconds.Text = _left.ToString();
        _timer.Tick += (_, _) =>
        {
            _left--;
            if (_left <= 0) { _timer.Stop(); Close(); return; }
            Seconds.Text = _left.ToString();
            Line.Text = $"This PC will {actionWord} in {_left} seconds.";
        };
        ContentRendered += (_, _) => { SystemSounds.Exclamation.Play(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
        if (!preview) StayOnTop.Enable(this, seconds);
    }
}
