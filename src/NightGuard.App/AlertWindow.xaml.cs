using System.Windows;
using System.Windows.Threading;

namespace NightGuard.App;

/// <summary>A warning or notice that pulls itself in front of games.</summary>
public partial class AlertWindow : Window
{
    public AlertWindow(string title, string text, bool urgent, int autoCloseSeconds, bool preview = false)
    {
        InitializeComponent();
        TitleText.Text = preview ? "PREVIEW: " + title : title;
        Body.Text = text;
        Header.Background = urgent ? Art.Alarm() : Art.NightSky();
        HeaderEmoji.Text = urgent ? "\U0001F6A8" : title.Contains("Screen", StringComparison.OrdinalIgnoreCase) ? "\U0001F3AE" : "\u23F0";
        if (urgent) HeaderEmoji.Foreground = System.Windows.Media.Brushes.White;
        if (!preview) StayOnTop.Enable(this, 5);
        if (autoCloseSeconds > 0)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoCloseSeconds) };
            t.Tick += (_, _) => { t.Stop(); Close(); };
            ContentRendered += (_, _) => t.Start();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
