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
        Root.Background = Art.NightSky();
        Art.Stars(Sky, 620, 420, 45, 11);
        MoonHolder.Children.Add(Art.Crescent(64, Art.Moon));
        var zzz = Art.Zzz(18);
        zzz.Margin = new Thickness(70, -6, 0, 0);
        zzz.HorizontalAlignment = HorizontalAlignment.Left;
        zzz.VerticalAlignment = VerticalAlignment.Top;
        MoonHolder.Children.Add(zzz);
        _left = seconds;
        Prefix.Text = preview ? "PREVIEW (nothing will happen) \U0001F440" : "It's bedtime. Goodnight! \U0001F634";
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
